using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OnebotServer.Api;
using OnebotServer.Core;

namespace OnebotServer.Transport;

internal enum WsRole
{
    Api,
    Event,
    Universal,
}

/// <summary>一条已建立的 WebSocket 连接（正向服务端与反向客户端共用）。</summary>
internal sealed class WsConnection
{
    public required WebSocket Socket { get; init; }

    public required WsRole Role { get; init; }

    public SemaphoreSlim SendLock { get; } = new(1, 1);

    public bool AcceptsApi => Role is WsRole.Api or WsRole.Universal;

    public bool AcceptsEvent => Role is WsRole.Event or WsRole.Universal;
}

/// <summary>WebSocket 收发与请求处理的公共逻辑。</summary>
internal static class WsCommon
{
    public const int MaxMessageChars = 4 * 1024 * 1024;

    /// <summary>
    /// 鉴权：access_token 为空表示不校验。
    /// 返回 0 通过，401 未提供 token，403 token 不匹配。
    /// </summary>
    public static int CheckAuth(string? authorizationHeader, string? queryAccessToken)
    {
        string token = PluginRuntime.Config.Auth.AccessToken;
        if (string.IsNullOrEmpty(token))
        {
            return 0;
        }

        string provided = "";
        if (!string.IsNullOrEmpty(authorizationHeader) &&
            authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            provided = authorizationHeader["Bearer ".Length..].Trim();
        }
        else if (!string.IsNullOrEmpty(queryAccessToken))
        {
            provided = queryAccessToken;
        }

        return provided.Length == 0 ? 401 : provided == token ? 0 : 403;
    }

    /// <summary>
    /// 尝试启动 HttpListener。通配符地址（0.0.0.0 等）在非管理员下会被 http.sys 拒绝，
    /// 此时自动回退为仅回环（127.0.0.1）监听并给出告警，保证服务至少本机可用。
    /// 成功返回 listener，失败返回 null。
    /// </summary>
    public static HttpListener? TryStartListener(string host, int port, string tag, out string prefix)
    {
        string wildcardPrefix = HttpApiServer.BuildPrefix(host, port);
        prefix = wildcardPrefix;
        var listener = new HttpListener();
        listener.Prefixes.Add(wildcardPrefix);
        try
        {
            listener.Start();
            return listener;
        }
        catch (HttpListenerException ex)
        {
            listener.Close();
            bool wildcard = string.IsNullOrWhiteSpace(host) || host is "0.0.0.0" or "::" or "::0" or "*" or "+";
            if (!wildcard || ex.NativeErrorCode != 5)
            {
                PluginRuntime.Log("Error", tag, $"服务启动失败（{wildcardPrefix}）: {ex.Message}。" +
                    "监听非本机地址时需要管理员权限，或执行 netsh http add urlacl 进行预留");
                return null;
            }

            string fallback = $"http://127.0.0.1:{port}/";
            var loopback = new HttpListener();
            loopback.Prefixes.Add(fallback);
            try
            {
                loopback.Start();
                prefix = fallback;
                PluginRuntime.Log("Warn", tag, $"无法监听 {wildcardPrefix}（{ex.Message}），已回退为仅本机访问 {fallback}。" +
                    "如需对外监听请以管理员运行框架，或执行 netsh http add urlacl 进行预留");
                return loopback;
            }
            catch (HttpListenerException ex2)
            {
                loopback.Close();
                PluginRuntime.Log("Error", tag, $"服务启动失败（{fallback}）: {ex2.Message}");
                return null;
            }
        }
    }

    /// <summary>持续接收文本消息并交给回调处理；收到关闭帧或断开时返回。</summary>
    public static async Task ReceiveLoopAsync(WebSocket socket, Func<string, Task> onText, CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var message = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
                    }
                    catch
                    {
                    }

                    return;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (message.Length > MaxMessageChars)
                    {
                        throw new InvalidDataException("WebSocket 消息超过大小限制");
                    }
                }
            }
            while (!result.EndOfMessage);

            if (message.Length > 0)
            {
                await onText(message.ToString());
            }
        }
    }

    /// <summary>发送 JSON 文本。同一连接上的发送需串行化。</summary>
    public static async Task SendJsonAsync(WebSocket socket, SemaphoreSlim sendLock, JsonNode node, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        await sendLock.WaitAsync(CancellationToken.None);
        try
        {
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            sendLock.Release();
        }
    }

    /// <summary>
    /// 处理一条 API 调用请求文本（{action, params, echo}），返回响应对象。
    /// 格式不正确返回 retcode 1400；仅 Api/Universal 连接应调用本方法。
    /// </summary>
    public static async Task<JsonObject> HandleRequestAsync(string json)
    {
        JsonNode? echo = null;
        bool hasEcho = false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj)
            {
                return ApiResponse.Fail(RetCode.BadRequest, null, false).ToJson();
            }

            string action = obj["action"] switch
            {
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                _ => "",
            };

            hasEcho = obj.ContainsKey("echo");
            echo = obj["echo"];
            var parameters = obj["params"] as JsonObject;

            if (action.Length == 0)
            {
                return ApiResponse.Fail(RetCode.BadRequest, echo, hasEcho).ToJson();
            }

            var response = await ApiDispatcher.DispatchAsync(action, parameters, echo, hasEcho);
            return response.ToJson();
        }
        catch (JsonException)
        {
            return ApiResponse.Fail(RetCode.BadRequest, echo, hasEcho).ToJson();
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Error", "API", $"处理请求失败: {ex.Message}");
            return ApiResponse.Fail(RetCode.Failed, echo, hasEcho).ToJson();
        }
    }
}
