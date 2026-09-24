using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using OnebotServer.Core;

namespace OnebotServer.Transport;

/// <summary>
/// HTTP POST 事件上报：向配置的 URL POST 事件 JSON。
/// 配置 secret 时附加 HMAC-SHA1 签名头 X-Signature，并处理响应中的快速操作。
/// </summary>
public sealed class HttpEventPoster
{
    private readonly HttpClient _client;

    public HttpEventPoster()
    {
        _client = new HttpClient();
        int timeout = PluginRuntime.Config.HttpPost.Timeout;
        _client.Timeout = timeout > 0 ? TimeSpan.FromSeconds(timeout) : System.Threading.Timeout.InfiniteTimeSpan;
    }

    /// <summary>上报事件，返回响应正文中的快速操作对象（若无则返回 null）。</summary>
    public async Task<JsonObject?> PostAsync(JsonObject evt)
    {
        var cfg = PluginRuntime.Config.HttpPost;
        if (string.IsNullOrWhiteSpace(cfg.Url))
        {
            return null;
        }

        try
        {
            byte[] body = Encoding.UTF8.GetBytes(evt.ToJsonString());
            using var request = new HttpRequestMessage(HttpMethod.Post, cfg.Url)
            {
                Content = new ByteArrayContent(body),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Headers.Add("X-Self-ID", PluginRuntime.SelfId.ToString());

            if (!string.IsNullOrEmpty(cfg.Secret))
            {
                using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(cfg.Secret));
                string signature = Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
                request.Headers.Add("X-Signature", $"sha1={signature}");
            }

            using var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                PluginRuntime.Log("Warn", "HTTP POST", $"事件上报返回 {(int)response.StatusCode}");
                return null;
            }

            string text = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return JsonNode.Parse(text) as JsonObject;
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Warn", "HTTP POST", $"事件上报失败: {ex.Message}");
            return null;
        }
    }
}
