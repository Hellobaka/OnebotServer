using Another_Mirai_Native.Abstractions.Context;
using Another_Mirai_Native.Abstractions.Enums;
using Another_Mirai_Native.Abstractions.Handlers;
using OnebotServer.Core;
using System.Text.Json.Nodes;

namespace OnebotServer.Events;

/// <summary>
/// AMN2 事件桥接：把框架事件转换为 OneBot v11 事件并投入分发队列。
/// 本插件仅观察事件，对其他插件一律放行（Pass）。
/// </summary>
public class EventBridge :
    IGroupMessageHandler,
    IPrivateMessageHandler,
    IGroupMemberIncreaseHandler,
    IGroupMemberDecreaseHandler,
    IGroupMemberBannedHandler,
    IGroupMemberUnbannedHandler,
    IAdminChangeHandler,
    IGroupWholeBannedHandler,
    IGroupWholeUnbannedHandler,
    IFriendAddedHandler,
    IGroupFileUploadHandler,
    IFriendAddRequestHandler,
    IGroupAddRequestHandler,
    IGroupInviteRequestHandler
{
    public async Task<EventHandleResult> OnReceiveGroupMessageAsync(GroupMessageContext e, CancellationToken ct)
    {
        PluginRuntime.RememberMessage(e.Message?.Id ?? 0, e.FromGroup.Id, true);
        return await PublishAsync(EventFactory.GroupMessageAsync(e));
    }

    public async Task<EventHandleResult> OnReceivePrivateMessageAsync(PrivateMessageContext e, CancellationToken ct)
    {
        PluginRuntime.RememberMessage(e.Message?.Id ?? 0, e.FromQQ.Id, false);
        return await PublishAsync(EventFactory.PrivateMessageAsync(e));
    }

    public async Task<EventHandleResult> OnGroupMemberIncreaseAsync(GroupMemberIncreaseContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.GroupMemberIncreaseAsync(e));

    public async Task<EventHandleResult> OnGroupMemberDecreaseAsync(GroupMemberDecreaseContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.GroupMemberDecreaseAsync(e));

    public async Task<EventHandleResult> OnGroupMemberBannedAsync(GroupMemberBannedContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.GroupMemberBannedAsync(e));

    public async Task<EventHandleResult> OnGroupMemberUnbannedAsync(GroupMemberUnbannedContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.GroupMemberUnbannedAsync(e));

    public async Task<EventHandleResult> OnAdminChangedAsync(AdminChangedContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.AdminChangedAsync(e));

    public async Task<EventHandleResult> OnGroupWholeBannedAsync(GroupWholeBannedContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.GroupWholeBannedAsync(e));

    public async Task<EventHandleResult> OnGroupWholeUnbannedAsync(GroupWholeUnbannedContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.GroupWholeUnbannedAsync(e));

    public async Task<EventHandleResult> OnFriendAddedAsync(FriendAddedContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.FriendAddedAsync(e));

    public async Task<EventHandleResult> OnGroupFileUploadedAsync(GroupFileUploadedContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.GroupFileUploadedAsync(e));

    public async Task<EventHandleResult> OnFriendAddRequestAsync(FriendAddRequestContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.FriendAddRequestAsync(e));

    public async Task<EventHandleResult> OnGroupAddRequestAsync(GroupAddRequestContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.GroupAddRequestAsync(e));

    public async Task<EventHandleResult> OnGroupInviteRequestAsync(GroupInviteRequestContext e, CancellationToken ct)
        => await PublishAsync(EventFactory.GroupInviteRequestAsync(e));

    private static async Task<EventHandleResult> PublishAsync(Task<JsonObject> eventTask)
    {
        try
        {
            EventDispatcher.Publish(await eventTask);
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Error", "事件", $"事件转换失败: {ex.Message}");
        }

        return EventHandleResult.Pass;
    }
}
