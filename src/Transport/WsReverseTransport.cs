using System.Net.WebSockets;
using System.Text.Json.Nodes;
using OnebotServer.Config;
using OnebotServer.Core;

namespace OnebotServer.Transport;

/// <summary>
/// 反向 WebSocket：OneBot 作为 WebSocket 客户端主动连接配置的 URL，
/// 按 API / Event / 三种角色建立连接并提供对应服务，断线后按配置间隔重连。
/// </summary>
public sealed class WsReverseTransport : IEventTransport
{
    private readonly List<WsConnection> _connections = new();
    private CancellationTokenSource? _cts;

    public string Name => "反向WebSocket";

    /// <summary>配置中是否存在有效的连接目标。</summary>
    public static bool HasTarget(WsReverseConfig cfg)
    {
        return cfg.UseUniversalClient
            ? !string.IsNullOrWhiteSpace(FirstNonEmpty(cfg.Url, cfg.ApiUrl, cfg.EventUrl))
            : !string.IsNullOrWhiteSpace(FirstNonEmpty(cfg.ApiUrl, cfg.Url))
                || !string.IsNullOrWhiteSpace(FirstNonEmpty(cfg.EventUrl, cfg.Url));
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        var cfg = PluginRuntime.Config.WsReverse;

        if (cfg.UseUniversalClient)
        {
            string url = FirstNonEmpty(cfg.Url, cfg.ApiUrl, cfg.EventUrl);
            if (url != "")
            {
                _ = Task.Run(() => MaintainAsync(WsRole.Universal, url, _cts.Token));
            }
        }
        else
        {
            string apiUrl = FirstNonEmpty(cfg.ApiUrl, cfg.Url);
            string eventUrl = FirstNonEmpty(cfg.EventUrl, cfg.Url);
            if (apiUrl != "")
            {
                _ = Task.Run(() => MaintainAsync(WsRole.Api, apiUrl, _cts.Token));
            }

            if (eventUrl != "")
            {
                _ = Task.Run(() => MaintainAsync(WsRole.Event, eventUrl, _cts.Token));
            }
        }
    }

    public void Stop()
    {
        _cts?.Cancel();

        List<WsConnection> connections;
        lock (_connections)
        {
            connections = _connections.ToList();
            _connections.Clear();
        }

        foreach (var conn in connections)
        {
            try
            {
                conn.Socket.Abort();
                conn.Socket.Dispose();
            }
            catch
            {
            }
        }

        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>向所有 Event 与 Universal 连接推送事件。</summary>
    public async Task PushEventAsync(JsonObject evt)
    {
        List<WsConnection> targets;
        lock (_connections)
        {
            targets = _connections.Where(c => c.AcceptsEvent).ToList();
        }

        foreach (var conn in targets)
        {
            try
            {
                await WsCommon.SendJsonAsync(conn.Socket, conn.SendLock, evt, CancellationToken.None);
            }
            catch (Exception ex)
            {
                PluginRuntime.Log("Debug", "反向WS", $"事件推送失败: {ex.Message}");
            }
        }
    }

    private async Task MaintainAsync(WsRole role, string url, CancellationToken ct)
    {
        string roleName = role == WsRole.Api ? "API" : role == WsRole.Event ? "Event" : "Universal";
        while (!ct.IsCancellationRequested)
        {
            var socket = new ClientWebSocket();
            WsConnection? conn = null;
            try
            {
                socket.Options.SetRequestHeader("X-Self-ID", PluginRuntime.SelfId.ToString());
                socket.Options.SetRequestHeader("X-Client-Role", roleName);
                string token = PluginRuntime.Config.Auth.AccessToken;
                if (!string.IsNullOrEmpty(token))
                {
                    socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
                }

                await socket.ConnectAsync(new Uri(url), ct);
                conn = new WsConnection { Socket = socket, Role = role };
                lock (_connections)
                {
                    _connections.Add(conn);
                }

                PluginRuntime.Log("Info", "反向WS", $"已连接 {url}（{roleName}）");

                // 连接建立后推送 lifecycle connect（仅 WebSocket 能收到）
                if (conn.AcceptsEvent)
                {
                    await WsCommon.SendJsonAsync(socket, conn.SendLock, EventFactory.Lifecycle("connect"), ct);
                }

                await WsCommon.ReceiveLoopAsync(socket, async text =>
                {
                    // Event 连接只推送事件，不提供 API 调用服务
                    if (!conn.AcceptsApi)
                    {
                        return;
                    }

                    var reply = await WsCommon.HandleRequestAsync(text);
                    await WsCommon.SendJsonAsync(socket, conn.SendLock, reply, ct);
                }, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                PluginRuntime.Log("Warn", "反向WS", $"{url}（{roleName}）连接断开: {ex.Message}");
            }
            catch
            {
            }
            finally
            {
                if (conn != null)
                {
                    lock (_connections)
                    {
                        _connections.Remove(conn);
                    }
                }

                try
                {
                    socket.Dispose();
                }
                catch
                {
                }
            }

            try
            {
                await Task.Delay(Math.Max(1000, PluginRuntime.Config.WsReverse.ReconnectInterval), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}
