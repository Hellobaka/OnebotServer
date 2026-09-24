using System.Text.Json;

namespace OnebotServer.Config;

/// <summary>
/// 插件配置，对应 OneBot v11 标准中各通信方式的「相关配置」项。
/// 配置文件以 JSON 形式保存在插件数据目录下的 config.json。
/// </summary>
public class PluginConfig
{
    public HttpConfig Http { get; set; } = new();

    public HttpPostConfig HttpPost { get; set; } = new();

    public WsConfig Ws { get; set; } = new();

    public WsReverseConfig WsReverse { get; set; } = new();

    public AuthConfig Auth { get; set; } = new();

    public EventConfig Event { get; set; } = new();

    public HeartbeatConfig Heartbeat { get; set; } = new();

    public RateLimitConfig Api { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>读取配置文件；文件不存在或损坏时写入默认配置并返回默认值。</summary>
    public static PluginConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var config = JsonSerializer.Deserialize<PluginConfig>(File.ReadAllText(path), JsonOptions);
                if (config != null)
                {
                    config.Normalize();
                    return config;
                }
            }
        }
        catch
        {
            // 配置损坏时回落到默认值并覆盖保存
        }

        var created = new PluginConfig();
        created.Save(path);
        return created;
    }

    public void Save(string path)
    {
        Normalize();
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>防止配置文件中出现 null 小节导致空引用。</summary>
    private void Normalize()
    {
        Http ??= new HttpConfig();
        HttpPost ??= new HttpPostConfig();
        Ws ??= new WsConfig();
        WsReverse ??= new WsReverseConfig();
        Auth ??= new AuthConfig();
        Event ??= new EventConfig();
        Heartbeat ??= new HeartbeatConfig();
        Api ??= new RateLimitConfig();
    }
}

/// <summary>HTTP 服务（OneBot 作为 HTTP 服务端提供 API 调用服务）。</summary>
public class HttpConfig
{
    /// <summary>是否启用 HTTP 服务。</summary>
    public bool Enable { get; set; } = true;

    /// <summary>HTTP 服务器监听的 IP。</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>HTTP 服务器监听的端口。</summary>
    public int Port { get; set; } = 5700;

    /// <summary>（保留）HTTP 请求超时时间，单位秒，0 表示不设置超时。</summary>
    public int Timeout { get; set; }
}

/// <summary>HTTP POST 服务（OneBot 作为 HTTP 客户端向配置的 URL 上报事件）。</summary>
public class HttpPostConfig
{
    /// <summary>是否启用 HTTP POST 事件上报。</summary>
    public bool Enable { get; set; } = true;

    /// <summary>事件上报 URL，为空时不进行上报。</summary>
    public string Url { get; set; } = "";

    /// <summary>HTTP 上报超时时间，单位秒，0 表示不设置超时。</summary>
    public int Timeout { get; set; }

    /// <summary>上报数据签名密钥，非空时添加 X-Signature 头。</summary>
    public string Secret { get; set; } = "";
}

/// <summary>正向 WebSocket 服务（OneBot 作为 WebSocket 服务端）。</summary>
public class WsConfig
{
    /// <summary>是否启用正向 WebSocket。</summary>
    public bool Enable { get; set; }

    /// <summary>WebSocket 服务器监听的 IP。</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>WebSocket 服务器监听的端口。</summary>
    public int Port { get; set; } = 6700;
}

/// <summary>反向 WebSocket 服务（OneBot 作为 WebSocket 客户端主动连接配置的 URL）。</summary>
public class WsReverseConfig
{
    /// <summary>是否启用反向 WebSocket。</summary>
    public bool Enable { get; set; } = true;

    /// <summary>反向 WebSocket API、Event、Universal 共用 URL。</summary>
    public string Url { get; set; } = "";

    /// <summary>反向 WebSocket API URL，为空时使用 Url。</summary>
    public string ApiUrl { get; set; } = "";

    /// <summary>反向 WebSocket Event URL，为空时使用 Url。</summary>
    public string EventUrl { get; set; } = "";

    /// <summary>是否使用 Universal 客户端（在一条连接上同时提供 API 与事件服务）。</summary>
    public bool UseUniversalClient { get; set; }

    /// <summary>断线重连间隔，单位毫秒。</summary>
    public int ReconnectInterval { get; set; } = 3000;
}

/// <summary>鉴权配置。</summary>
public class AuthConfig
{
    /// <summary>access token，非空时启用鉴权。</summary>
    public string AccessToken { get; set; } = "";
}

/// <summary>事件格式配置。</summary>
public class EventConfig
{
    /// <summary>事件数据中的消息格式：string（CQ 码字符串）或 array（消息段数组）。</summary>
    public string MessageFormat { get; set; } = "string";
}

/// <summary>心跳配置。</summary>
public class HeartbeatConfig
{
    /// <summary>是否启用心跳机制。</summary>
    public bool Enable { get; set; }

    /// <summary>产生心跳元事件的时间间隔，单位毫秒。</summary>
    public int Interval { get; set; } = 15000;
}

/// <summary>API 调用配置。</summary>
public class RateLimitConfig
{
    /// <summary>限速 API 调用的排队间隔时间，单位毫秒。</summary>
    public int RateLimitInterval { get; set; } = 500;
}
