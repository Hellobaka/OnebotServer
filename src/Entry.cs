using Another_Mirai_Native.Abstractions;
using Another_Mirai_Native.Abstractions.Attributes;
using OnebotServer.Core;

namespace OnebotServer;

/// <summary>
/// OneBot v11 服务插件入口：按配置启动 HTTP、HTTP POST、正向 WebSocket、反向 WebSocket 服务，
/// 把 AMN2 的消息/通知/请求/元事件转换为 OneBot v11 事件对外提供。
/// </summary>
[PluginInfo(
    "me.cqp.luohuaming.Onebot",
    "OneBot v11 服务",
    "1.0.0",
    "基于 OneBot v11 标准提供 HTTP、HTTP POST、正向 WebSocket、反向 WebSocket 通信服务",
    "Copilot")]
public class Entry : PluginBase
{
    public override async Task OnEnableAsync(CancellationToken ct)
    {
        PluginRuntime.Initialize(API);
        PluginRuntime.Start();
        await Task.CompletedTask;
    }

    public override async Task OnDisableAsync(CancellationToken ct)
    {
        await PluginRuntime.StopAsync();
    }
}
