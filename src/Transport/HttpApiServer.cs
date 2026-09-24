using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OnebotServer.Api;
using OnebotServer.Core;

namespace OnebotServer.Transport;

/// <summary>
/// HTTP 服务：OneBot 作为 HTTP 服务端，接受路径为 /:action 的 API 请求。
/// 支持 GET（query 参数）与 POST（urlencoded 表单 / JSON）。
/// </summary>
public sealed class HttpApiServer
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public void Start()
    {
        var cfg = PluginRuntime.Config.Http;
        _cts = new CancellationTokenSource();
        var listener = WsCommon.TryStartListener(cfg.Host, cfg.Port, "HTTP", out string prefix);
        if (listener == null)
        {
            return;
        }

        _listener = listener;
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        PluginRuntime.Log("Info", "HTTP", $"HTTP 服务已监听 {prefix}");
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
        }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    internal static string BuildPrefix(string host, int port)
    {
        string h = string.IsNullOrWhiteSpace(host) || host is "0.0.0.0" or "::" or "::0" or "*" or "+"
            ? "+"
            : host.Contains(':') ? $"[{host}]" : host;
        return $"http://{h}:{port}/";
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch
            {
                continue;
            }

            _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            int authStatus = WsCommon.CheckAuth(request.Headers["Authorization"], request.QueryString["access_token"]);
            if (authStatus != 0)
            {
                response.StatusCode = authStatus;
                response.Close();
                return;
            }

            string action = Uri.UnescapeDataString(request.Url?.AbsolutePath.Trim('/') ?? "");
            if (action.Length == 0)
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            JsonObject? parameters;
            if (request.HttpMethod == "GET")
            {
                parameters = QueryToJson(request.QueryString);
            }
            else if (request.HttpMethod == "POST")
            {
                string contentType = (request.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
                string body;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync();
                }

                if (contentType == "application/json")
                {
                    try
                    {
                        if (JsonNode.Parse(body) is JsonObject obj)
                        {
                            parameters = obj;
                        }
                        else
                        {
                            response.StatusCode = 400;
                            response.Close();
                            return;
                        }
                    }
                    catch (JsonException)
                    {
                        response.StatusCode = 400;
                        response.Close();
                        return;
                    }
                }
                else if (contentType == "application/x-www-form-urlencoded")
                {
                    parameters = FormToJson(body);
                }
                else
                {
                    response.StatusCode = 406;
                    response.Close();
                    return;
                }
            }
            else
            {
                response.StatusCode = 405;
                response.Close();
                return;
            }

            // 允许 HTTP 调用方以 echo 参数要求原样回显
            JsonNode? echo = null;
            bool hasEcho = false;
            if (parameters!.ContainsKey("echo"))
            {
                hasEcho = true;
                echo = parameters["echo"];
                parameters.Remove("echo");
            }

            var apiResponse = await ApiDispatcher.DispatchAsync(action, parameters, echo, hasEcho);

            response.StatusCode = apiResponse.HttpStatus;
            if (apiResponse.HttpStatus == 200)
            {
                byte[] payload = Encoding.UTF8.GetBytes(apiResponse.ToJson().ToJsonString());
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = payload.Length;
                await response.OutputStream.WriteAsync(payload);
            }

            response.Close();
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Debug", "HTTP", $"请求处理结束: {ex.Message}");
            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch
            {
            }
        }
    }

    private static JsonObject QueryToJson(NameValueCollection query)
    {
        var obj = new JsonObject();
        foreach (var key in query.AllKeys)
        {
            if (string.IsNullOrEmpty(key) || key == "access_token")
            {
                continue;
            }

            obj[key] = query[key];
        }

        return obj;
    }

    private static JsonObject FormToJson(string body)
    {
        var obj = new JsonObject();
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string key = eq < 0 ? pair : pair[..eq];
            string value = eq < 0 ? "" : pair[(eq + 1)..];
            key = Uri.UnescapeDataString(key.Replace('+', ' '));
            if (key == "access_token")
            {
                continue;
            }

            obj[key] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return obj;
    }
}
