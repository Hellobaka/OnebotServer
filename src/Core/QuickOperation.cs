using System.Text.Json.Nodes;

namespace OnebotServer.Core;

/// <summary>
/// 快速操作：在 HTTP POST 上报的响应正文或 .handle_quick_operation 中指定的简单操作。
/// 标准规定每个字段都是可选的，仅在字段存在时才触发相应操作。
/// </summary>
public static class QuickOperation
{
    public static async Task ExecuteAsync(JsonObject context, JsonObject operation)
    {
        try
        {
            string postType = GetString(context, "post_type");
            switch (postType)
            {
                case "message":
                    await MessageAsync(context, operation);
                    break;
                case "request":
                    await RequestAsync(context, operation);
                    break;
            }
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Error", "快速操作", $"执行快速操作失败: {ex.Message}");
        }
    }

    private static async Task MessageAsync(JsonObject context, JsonObject operation)
    {
        var api = PluginRuntime.Api;
        long userId = GetLong(context, "user_id");
        long groupId = GetLong(context, "group_id");
        int messageId = (int)GetLong(context, "message_id");
        bool isGroup = GetString(context, "message_type") == "group" && groupId != 0;

        if (operation.ContainsKey("reply"))
        {
            bool autoEscape = GetBool(operation, "auto_escape", false);
            string cq = await MessageConverter.ToCqStringAsync(operation["reply"], autoEscape);

            // 标准默认值：群消息快速回复默认 at 发送者
            if (isGroup && GetBool(operation, "at_sender", true) && userId != 0)
            {
                cq = $"[CQ:at,qq={userId}] " + cq;
            }

            if (isGroup)
            {
                int id = await api.MessageApi.SendGroupMessageAsync(groupId, cq);
                PluginRuntime.RememberMessage(id, groupId, true);
            }
            else
            {
                int id = await api.MessageApi.SendPrivateMessageAsync(userId, cq);
                PluginRuntime.RememberMessage(id, userId, false);
            }
        }

        if (GetBool(operation, "delete", false) && messageId != 0)
        {
            await api.MessageApi.DeleteMessageAsync(messageId);
        }

        if (isGroup && userId != 0)
        {
            if (GetBool(operation, "kick", false))
            {
                await api.GroupApi.KickAsync(groupId, userId, false);
            }

            if (GetBool(operation, "ban", false))
            {
                long duration = operation.ContainsKey("ban_duration") ? GetLong(operation, "ban_duration") : 30 * 60;
                await api.GroupApi.BanMemberAsync(groupId, userId, duration);
            }
        }
    }

    private static async Task RequestAsync(JsonObject context, JsonObject operation)
    {
        // 默认不处理，仅在明确给出 approve 字段时触发
        if (!operation.ContainsKey("approve"))
        {
            return;
        }

        var api = PluginRuntime.Api;
        bool approve = GetBool(operation, "approve", true);
        string flag = GetString(context, "flag");

        switch (GetString(context, "request_type"))
        {
            case "friend":
                await api.FriendApi.SetFriendAddRequestAsync(flag, approve, GetString(operation, "remark"));
                break;
            case "group":
                string reason = GetString(operation, "reason");
                if (GetString(context, "sub_type") == "invite")
                {
                    await api.GroupApi.SetGroupInviteRequestAsync(flag, approve, reason);
                }
                else
                {
                    await api.GroupApi.SetGroupAddRequestAsync(flag, approve, reason);
                }

                break;
        }
    }

    private static string GetString(JsonObject obj, string key) => JsonValues.ToText(obj[key]);

    private static long GetLong(JsonObject obj, string key)
        => JsonValues.TryGetLong(obj[key], out var value) ? value : 0;

    private static bool GetBool(JsonObject obj, string key, bool def)
        => JsonValues.TryGetBool(obj[key], out var value) ? value : def;
}
