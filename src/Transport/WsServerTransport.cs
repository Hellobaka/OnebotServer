using System.Net;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using OnebotServer.Core;

namespace OnebotServer.Transport;

/// <summary>
/// 正向 WebSocket 服务：OneBot 作为 WebSocket 服务端，
/// 提供 /api（API 调用）、/event（事件推送）、/（两者兼备）三种接口。
/// </summary>
public sealed class WsServerTransport : IEventTransport
{
    private readonly List<WsConnection> _connections = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public string Name => "正向WebSocket";

    public void Start()
    {
        var cfg = PluginRuntime.Config.Ws;
        _cts = new CancellationTokenSource();
        var listener = WsCommon.TryStartListener(cfg.Host, cfg.Port, "正向WS", out string prefix);
        if (listener == null)
        {
            return;
        }

        _listener = listener;
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        PluginRuntime.Log("Info", "正向WS", $"正向 WebSocket 服务已监听 {prefix}");
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

    /// <summary>向所有 /event 与 / 连接推送事件。</summary>
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
                PluginRuntime.Log("Debug", "正向WS", $"事件推送失败: {ex.Message}");
            }
        }
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

            _ = Task.Run(() => HandleContextAsync(context, ct), CancellationToken.None);
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;
        WsConnection? conn = null;
        try
        {
            if (!request.IsWebSocketRequest)
            {
                response.StatusCode = 400;
                response.Close();
                return;
            }

            string path = request.Url?.AbsolutePath ?? "/";
            WsRole? role = path switch
            {
                "/api" or "/api/" => WsRole.Api,
                "/event" or "/event/" => WsRole.Event,
                "/" => WsRole.Universal,
                _ => null,
            };
            if (role == null)
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            int authStatus = WsCommon.CheckAuth(request.Headers["Authorization"], request.QueryString["access_token"]);
            if (authStatus != 0)
            {
                // 鉴权失败直接断开，不进入 API 调用阶段
                response.StatusCode = authStatus;
                response.Close();
                return;
            }

            // 客户端声明了子协议时选择其第一个，未声明则不选择
            string? subProtocol = request.Headers["Sec-WebSocket-Protocol"]
                ?.Split(',')
                .Select(s => s.Trim())
                .FirstOrDefault(s => s.Length > 0);

            var wsContext = await context.AcceptWebSocketAsync(subProtocol);
            conn = new WsConnection { Socket = wsContext.WebSocket, Role = role.Value };
            lock (_connections)
            {
                _connections.Add(conn);
            }

            PluginRuntime.Log("Info", "正向WS", $"客户端已连接 {path}（{role}）");

            // 连接建立后推送 lifecycle connect（仅 WebSocket 能收到）
            if (conn.AcceptsEvent)
            {
                await WsCommon.SendJsonAsync(conn.Socket, conn.SendLock, EventFactory.Lifecycle("connect"), ct);
            }

            await WsCommon.ReceiveLoopAsync(conn.Socket, async text =>
            {
                // /event 接口只推送事件，不提供 API 调用服务
                if (!conn.AcceptsApi)
                {
                    return;
                }

                var reply = await WsCommon.HandleRequestAsync(text);
                await WsCommon.SendJsonAsync(conn.Socket, conn.SendLock, reply, ct);
            }, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            PluginRuntime.Log("Debug", "正向WS", $"连接结束: {ex.Message}");
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

                try
                {
                    conn.Socket.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}
