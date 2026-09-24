using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace OnebotServer.Core;

/// <summary>
/// 事件分发中心。事件按标准顺序处理：
/// 先通过 HTTP POST 上报并处理其响应中的快速操作，再向所有正向/反向 WebSocket 的
/// Event / Universal 连接推送。使用单队列串行处理保证事件顺序。
/// </summary>
public static class EventDispatcher
{
    private static Channel<JsonObject>? _channel;
    private static Task? _pump;
    private static Task? _heartbeat;
    private static CancellationTokenSource? _cts;

    public static void Start()
    {
        _cts = new CancellationTokenSource();
        _channel = Channel.CreateUnbounded<JsonObject>(new UnboundedChannelOptions { SingleReader = true });
        _pump = Task.Run(() => PumpAsync(_cts.Token));

        if (PluginRuntime.Config.Heartbeat.Enable)
        {
            _heartbeat = Task.Run(() => HeartbeatAsync(_cts.Token));
        }
    }

    public static async Task StopAsync()
    {
        // 生命周期 disable 仅通过 HTTP POST 上报
        var poster = PluginRuntime.Poster;
        if (poster != null)
        {
            try
            {
                await poster.PostAsync(EventFactory.Lifecycle("disable"));
            }
            catch
            {
                // 上报失败不阻塞停用
            }
        }

        _cts?.Cancel();
        _channel?.Writer.TryComplete();

        foreach (var task in new[] { _pump, _heartbeat })
        {
            try
            {
                if (task != null)
                {
                    await task.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch
            {
                // 超时或取消均忽略
            }
        }

        _pump = null;
        _heartbeat = null;
        _cts?.Dispose();
        _cts = null;
        _channel = null;
    }

    /// <summary>将事件投入分发队列（非阻塞）。</summary>
    public static void Publish(JsonObject evt)
    {
        _channel?.Writer.TryWrite(evt);
    }

    private static async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _channel!.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var poster = PluginRuntime.Poster;
                    if (poster != null)
                    {
                        var quickOperation = await poster.PostAsync(evt);
                        if (quickOperation != null)
                        {
                            await QuickOperation.ExecuteAsync(evt, quickOperation);
                        }
                    }

                    foreach (var sink in PluginRuntime.EventSinks)
                    {
                        try
                        {
                            await sink.PushEventAsync(evt);
                        }
                        catch (Exception ex)
                        {
                            PluginRuntime.Log("Debug", "事件", $"推送至 {sink.Name} 失败: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    PluginRuntime.Log("Error", "事件", $"事件处理失败: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task HeartbeatAsync(CancellationToken ct)
    {
        int interval = Math.Max(1000, PluginRuntime.Config.Heartbeat.Interval);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(interval));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                Publish(EventFactory.Heartbeat(interval));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
