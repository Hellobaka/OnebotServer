using OnebotServer.Api;
using OnebotServer.Transport;
using OnebotServer.Config;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace OnebotServer.Core;

/// <summary>插件运行时：承载 API 引用、配置、目录、服务实例与消息 ID 映射。</summary>
public static class PluginRuntime
{
    public static Another_Mirai_Native.Abstractions.Services.IPluginApi Api { get; private set; } = null!;

    public static PluginConfig Config { get; private set; } = new();

    /// <summary>框架分配给插件的数据目录。</summary>
    public static string AppDir { get; private set; } = "";

    public static string ConfigPath => Path.Combine(AppDir, "config.json");

    /// <summary>框架根目录。</summary>
    public static string FrameworkRoot { get; private set; } = "";

    public static string ImageDir => Path.Combine(FrameworkRoot, "data", "image");

    public static string RecordDir => Path.Combine(FrameworkRoot, "data", "record");

    private static HttpEventPoster? _poster;
    private static HttpApiServer? _http;

    /// <summary>HTTP POST 事件上报器，未启用或未配置 URL 时为 null。</summary>
    public static HttpEventPoster? Poster => _poster;

    private static readonly List<IEventTransport> Sinks = new();

    /// <summary>接收事件推送的传输层（正向 WS、反向 WS）。</summary>
    public static IReadOnlyList<IEventTransport> EventSinks
    {
        get
        {
            lock (Sinks)
            {
                return Sinks.ToList();
            }
        }
    }

    /// <summary>message_id → (来源群号或 QQ, 是否群聊) 的映射，供 get_msg 使用。</summary>
    public static readonly ConcurrentDictionary<int, (long ParentId, bool IsGroup)> MessageOrigins = new();

    private static readonly Queue<int> MessageOriginOrder = new();
    private static long _selfId;
    private static DateTimeOffset _selfIdTime;

    /// <summary>当前登录的机器人 QQ 号（5 秒缓存），未登录为 0。</summary>
    public static long SelfId
    {
        get
        {
            if (DateTimeOffset.Now - _selfIdTime > TimeSpan.FromSeconds(5))
            {
                try
                {
                    _selfId = Api?.AppApi.GetLoginQQ() ?? 0;
                }
                catch
                {
                    // RPC 不可用时沿用旧值
                }

                _selfIdTime = DateTimeOffset.Now;
            }

            return _selfId;
        }
    }

    public static bool IsOnline => SelfId != 0;

    public static void Initialize(Another_Mirai_Native.Abstractions.Services.IPluginApi api)
    {
        Api = api;
        AppDir = api.AppApi.GetAppDirectory();
        Config = PluginConfig.Load(ConfigPath);
        FrameworkRoot = ResolveFrameworkRoot();
        Directory.CreateDirectory(ImageDir);
        Directory.CreateDirectory(RecordDir);
    }

    /// <summary>按配置启动所有已启用的服务。</summary>
    public static void Start()
    {
        var started = new List<string>();

        if (Config.HttpPost.Enable && !string.IsNullOrWhiteSpace(Config.HttpPost.Url))
        {
            _poster = new HttpEventPoster();
            started.Add("HTTP POST");
            // 生命周期 enable 仅通过 HTTP POST 上报
            _ = _poster.PostAsync(EventFactory.Lifecycle("enable"));
        }

        if (Config.Http.Enable)
        {
            _http = new HttpApiServer();
            _http.Start();
            started.Add($"HTTP({Config.Http.Host}:{Config.Http.Port})");
        }

        if (Config.Ws.Enable)
        {
            var ws = new WsServerTransport();
            ws.Start();
            lock (Sinks)
            {
                Sinks.Add(ws);
            }

            started.Add($"正向WS({Config.Ws.Host}:{Config.Ws.Port})");
        }

        if (Config.WsReverse.Enable && WsReverseTransport.HasTarget(Config.WsReverse))
        {
            var wsReverse = new WsReverseTransport();
            wsReverse.Start();
            lock (Sinks)
            {
                Sinks.Add(wsReverse);
            }

            started.Add("反向WS");
        }

        EventDispatcher.Start();

        Log("Info", "服务", started.Count > 0
            ? $"OneBot v11 服务已启动: {string.Join(", ", started)}"
            : "OneBot v11 服务未启用任何通信方式，请检查配置");
    }

    /// <summary>停止所有服务（发送生命周期 disable 后关闭监听与连接）。</summary>
    public static async Task StopAsync()
    {
        await EventDispatcher.StopAsync();

        lock (Sinks)
        {
            foreach (var sink in Sinks)
            {
                try
                {
                    sink.Stop();
                }
                catch
                {
                }
            }

            Sinks.Clear();
        }

        try
        {
            _http?.Stop();
        }
        catch
        {
        }

        _http = null;
        _poster = null;
        Log("Info", "服务", "OneBot v11 服务已停止");
    }

    /// <summary>重新加载配置并重启所有服务。</summary>
    public static async Task ReloadAsync()
    {
        await StopAsync();
        Config = PluginConfig.Load(ConfigPath);
        Start();
        Log("Info", "服务", "配置已重载");
    }

    public static void RememberMessage(int messageId, long parentId, bool isGroup)
    {
        if (messageId == 0)
        {
            return;
        }

        MessageOrigins[messageId] = (parentId, isGroup);
        lock (MessageOriginOrder)
        {
            MessageOriginOrder.Enqueue(messageId);
            while (MessageOriginOrder.Count > 2000 && MessageOrigins.TryRemove(MessageOriginOrder.Dequeue(), out _))
            {
            }
        }
    }

    public static void Log(string level, string tag, string message)
    {
        try
        {
            switch (level)
            {
                case "Debug":
                    Api?.Logger.Debug(tag, message);
                    break;
                case "Info":
                    Api?.Logger.Info(tag, message);
                    break;
                case "Warn":
                    Api?.Logger.Warn(tag, message);
                    break;
                case "Error":
                    Api?.Logger.Error(tag, message);
                    break;
                case "Fatal":
                    Api?.Logger.Fatal(message);
                    break;
            }
        }
        catch
        {
            // 日志通道不可用时静默
        }
    }

    private static string ResolveFrameworkRoot()
    {
        // 方法 1：框架会把插件进程的工作目录设为框架根目录
        try
        {
            string cwd = Directory.GetCurrentDirectory();
            if (Directory.Exists(Path.Combine(cwd, "data", "image")) || Directory.Exists(Path.Combine(cwd, "data", "plugins")))
            {
                return cwd;
            }
        }
        catch
        {
        }

        // 方法 2：从插件数据目录向上查找包含 data\image 的目录
        try
        {
            var dir = new DirectoryInfo(AppDir);
            for (int i = 0; i < 5 && dir != null; i++, dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "data", "image")))
                {
                    return dir.FullName;
                }
            }
        }
        catch
        {
        }

        return Directory.GetCurrentDirectory();
    }
}
