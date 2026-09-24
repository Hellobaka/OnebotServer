using Another_Mirai_Native.Abstractions.Context;
using Another_Mirai_Native.Abstractions.Enums;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;

namespace OnebotServer.Core;

/// <summary>把 AMN2 事件上下文转换为 OneBot v11 事件 JSON。</summary>
public static class EventFactory
{
    private static readonly object CacheLock = new();
    private static readonly Dictionary<(long Group, long QQ), (DateTimeOffset Time, JsonObject Sender)> GroupSenderCache = new();
    private static readonly Dictionary<long, (DateTimeOffset Time, string Nick)> UserCache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    // ---------- 通用 ----------

    public static long UnixSeconds(DateTime time) => new DateTimeOffset(time).ToUnixTimeSeconds();

    public static long UnixNow() => DateTimeOffset.Now.ToUnixTimeSeconds();

    public static string SexString(QQSex sex) => sex switch
    {
        QQSex.Man => "male",
        QQSex.Woman => "female",
        _ => "unknown",
    };

    public static string RoleString(QQGroupMemberType type) => type switch
    {
        QQGroupMemberType.Creator => "owner",
        QQGroupMemberType.Manage => "admin",
        _ => "member",
    };

    public static JsonObject StatusObject() => new()
    {
        ["online"] = PluginRuntime.IsOnline,
        ["good"] = PluginRuntime.IsOnline,
    };

    private static JsonObject NewEvent(string postType, DateTimeOffset? time = null) => new()
    {
        ["time"] = (time ?? DateTimeOffset.Now).ToUnixTimeSeconds(),
        ["self_id"] = PluginRuntime.SelfId,
        ["post_type"] = postType,
    };

    // ---------- 消息事件 ----------

    public static async Task<JsonObject> GroupMessageAsync(GroupMessageContext e)
    {
        string raw = e.Message?.Text ?? "";
        var evt = NewEvent("message");
        evt["message_type"] = "group";
        evt["sub_type"] = "normal";
        evt["message_id"] = e.Message?.Id ?? 0;
        evt["group_id"] = e.FromGroup.Id;
        evt["user_id"] = e.FromQQ.Id;
        evt["anonymous"] = null;
        evt["message"] = MessageConverter.BuildMessageField(raw);
        evt["raw_message"] = raw;
        evt["font"] = 0;
        evt["sender"] = await GetGroupSenderAsync(e.FromGroup.Id, e.FromQQ.Id);
        return evt;
    }

    public static async Task<JsonObject> PrivateMessageAsync(PrivateMessageContext e)
    {
        string raw = e.Message?.Text ?? "";
        var evt = NewEvent("message");
        evt["message_type"] = "private";
        evt["sub_type"] = "friend";
        evt["message_id"] = e.Message?.Id ?? 0;
        evt["user_id"] = e.FromQQ.Id;
        evt["message"] = MessageConverter.BuildMessageField(raw);
        evt["raw_message"] = raw;
        evt["font"] = 0;
        evt["sender"] = await GetPrivateSenderAsync(e.FromQQ.Id);
        return evt;
    }

    // ---------- 通知事件 ----------

    public static Task<JsonObject> GroupMemberIncreaseAsync(GroupMemberIncreaseContext e)
    {
        var evt = NewEvent("notice", e.SendTime);
        evt["notice_type"] = "group_increase";
        evt["sub_type"] = e.IsInvited ? "invite" : "approve";
        evt["group_id"] = e.FromGroup.Id;
        evt["operator_id"] = e.FromQQ.Id;
        evt["user_id"] = e.BeingOperateQQ.Id;
        return Task.FromResult(evt);
    }

    public static Task<JsonObject> GroupMemberDecreaseAsync(GroupMemberDecreaseContext e)
    {
        long self = PluginRuntime.SelfId;
        string subType = !e.IsKicked
            ? "leave"
            : e.BeingOperateQQ.Id == self ? "kick_me" : "kick";
        var evt = NewEvent("notice", e.SendTime);
        evt["notice_type"] = "group_decrease";
        evt["sub_type"] = subType;
        evt["group_id"] = e.FromGroup.Id;
        evt["operator_id"] = subType == "leave" ? e.BeingOperateQQ.Id : e.FromQQ.Id;
        evt["user_id"] = e.BeingOperateQQ.Id;
        return Task.FromResult(evt);
    }

    public static Task<JsonObject> GroupMemberBannedAsync(GroupMemberBannedContext e)
    {
        var evt = NewEvent("notice", e.SendTime);
        evt["notice_type"] = "group_ban";
        evt["sub_type"] = "ban";
        evt["group_id"] = e.FromGroup.Id;
        evt["operator_id"] = e.FromQQ.Id;
        evt["user_id"] = e.BeingOperateQQ.Id;
        evt["duration"] = (long)(e.BanSpeakTimeSpan?.TotalSeconds ?? 0);
        return Task.FromResult(evt);
    }

    public static Task<JsonObject> GroupMemberUnbannedAsync(GroupMemberUnbannedContext e)
    {
        var evt = NewEvent("notice", e.SendTime);
        evt["notice_type"] = "group_ban";
        evt["sub_type"] = "lift_ban";
        evt["group_id"] = e.FromGroup.Id;
        evt["operator_id"] = e.FromQQ.Id;
        evt["user_id"] = e.BeingOperateQQ.Id;
        evt["duration"] = 0;
        return Task.FromResult(evt);
    }

    public static Task<JsonObject> AdminChangedAsync(AdminChangedContext e)
    {
        var evt = NewEvent("notice", e.SendTime);
        evt["notice_type"] = "group_admin";
        evt["sub_type"] = e.AdminChangedType == AdminChangedType.SetManage ? "set" : "unset";
        evt["group_id"] = e.FromGroup.Id;
        evt["user_id"] = e.BeingOperateQQ.Id;
        return Task.FromResult(evt);
    }

    /// <summary>
    /// 全员禁言。OneBot v11 标准未定义该事件，
    /// 这里作为扩展使用 notice/group_ban + sub_type whole_ban 上报（user_id 为 0）。
    /// </summary>
    public static Task<JsonObject> GroupWholeBannedAsync(GroupWholeBannedContext e)
    {
        var evt = NewEvent("notice", e.SendTime);
        evt["notice_type"] = "group_ban";
        evt["sub_type"] = "whole_ban";
        evt["group_id"] = e.FromGroup.Id;
        evt["operator_id"] = e.FromQQ.Id;
        evt["user_id"] = 0;
        evt["duration"] = 0;
        return Task.FromResult(evt);
    }

    /// <summary>全员禁言解除，扩展事件 sub_type whole_lift_ban。</summary>
    public static Task<JsonObject> GroupWholeUnbannedAsync(GroupWholeUnbannedContext e)
    {
        var evt = NewEvent("notice", e.SendTime);
        evt["notice_type"] = "group_ban";
        evt["sub_type"] = "whole_lift_ban";
        evt["group_id"] = e.FromGroup.Id;
        evt["operator_id"] = e.FromQQ.Id;
        evt["user_id"] = 0;
        evt["duration"] = 0;
        return Task.FromResult(evt);
    }

    public static Task<JsonObject> FriendAddedAsync(FriendAddedContext e)
    {
        var evt = NewEvent("notice", e.SendTime);
        evt["notice_type"] = "friend_add";
        evt["user_id"] = e.FromQQ.Id;
        return Task.FromResult(evt);
    }

    public static Task<JsonObject> GroupFileUploadedAsync(GroupFileUploadedContext e)
    {
        var evt = NewEvent("notice", e.SendTime);
        evt["notice_type"] = "group_upload";
        evt["group_id"] = e.FromGroup.Id;
        evt["user_id"] = e.FromQQ.Id;
        evt["file"] = new JsonObject
        {
            ["id"] = e.FileInfo?.FileId ?? "",
            ["name"] = e.FileInfo?.FileName ?? "",
            ["size"] = e.FileInfo?.FileSize ?? 0,
            ["busid"] = e.FileInfo?.Id ?? 0,
        };
        return Task.FromResult(evt);
    }

    // ---------- 请求事件 ----------

    public static Task<JsonObject> FriendAddRequestAsync(FriendAddRequestContext e)
    {
        var evt = NewEvent("request", e.SendTime);
        evt["request_type"] = "friend";
        evt["user_id"] = e.FromQQ.Id;
        evt["comment"] = e.AppendMessage ?? "";
        evt["flag"] = RequestFlagOf(e);
        return Task.FromResult(evt);
    }

    public static Task<JsonObject> GroupAddRequestAsync(GroupAddRequestContext e)
    {
        var evt = NewEvent("request", e.SendTime);
        evt["request_type"] = "group";
        evt["sub_type"] = "add";
        evt["group_id"] = e.FromGroup.Id;
        evt["user_id"] = e.FromQQ.Id;
        evt["comment"] = e.AppendMessage ?? "";
        evt["flag"] = RequestFlagOf(e);
        return Task.FromResult(evt);
    }

    public static Task<JsonObject> GroupInviteRequestAsync(GroupInviteRequestContext e)
    {
        var evt = NewEvent("request", e.SendTime);
        evt["request_type"] = "group";
        evt["sub_type"] = "invite";
        evt["group_id"] = e.FromGroup.Id;
        evt["user_id"] = e.FromQQ.Id;
        evt["comment"] = e.AppendMessage ?? "";
        evt["flag"] = RequestFlagOf(e);
        return Task.FromResult(evt);
    }

    // ---------- 元事件 ----------

    public static JsonObject Lifecycle(string subType) => new()
    {
        ["time"] = UnixNow(),
        ["self_id"] = PluginRuntime.SelfId,
        ["post_type"] = "meta_event",
        ["meta_event_type"] = "lifecycle",
        ["sub_type"] = subType,
    };

    public static JsonObject Heartbeat(long intervalMs) => new()
    {
        ["time"] = UnixNow(),
        ["self_id"] = PluginRuntime.SelfId,
        ["post_type"] = "meta_event",
        ["meta_event_type"] = "heartbeat",
        ["status"] = StatusObject(),
        ["interval"] = intervalMs,
    };

    // ---------- 发送人信息 ----------

    /// <summary>群消息发送人信息（尽量补齐 OneBot 规定的字段）。</summary>
    public static async Task<JsonObject> GetGroupSenderAsync(long groupId, long qq)
    {
        lock (CacheLock)
        {
            if (GroupSenderCache.TryGetValue((groupId, qq), out var cached) && DateTimeOffset.Now - cached.Time < CacheTtl)
            {
                // JsonNode 只允许存在一个父节点，缓存对象复用时返回克隆
                return (JsonObject)cached.Sender.DeepClone();
            }
        }

        JsonObject? sender = null;
        try
        {
            var info = await PluginRuntime.Api.GroupApi.GetGroupMemberInfoAsync(groupId, qq);

            // 框架对不存在的成员会返回全空对象（QQ 为 0），此处按未找到处理
            if (info != null && info.QQ != 0)
            {
                sender = new JsonObject
                {
                    ["user_id"] = info.QQ,
                    ["nickname"] = info.Nick ?? "",
                    ["card"] = info.Card ?? "",
                    ["sex"] = SexString(info.Sex),
                    ["age"] = info.Age,
                    ["area"] = info.Area ?? "",
                    ["level"] = info.Level ?? "",
                    ["role"] = RoleString(info.MemberType),
                    ["title"] = info.ExclusiveTitle ?? "",
                };
            }
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Debug", "事件", $"获取群成员信息失败: {ex.Message}");
        }

        if (sender == null)
        {
            // 尽最大努力：退化为好友昵称
            sender = new JsonObject
            {
                ["user_id"] = qq,
                ["nickname"] = await GetFriendNickAsync(qq),
            };
        }

        lock (CacheLock)
        {
            GroupSenderCache[(groupId, qq)] = (DateTimeOffset.Now, sender);
            if (GroupSenderCache.Count > 500)
            {
                foreach (var key in GroupSenderCache.Where(kv => DateTimeOffset.Now - kv.Value.Time >= CacheTtl).Select(kv => kv.Key).ToList())
                {
                    GroupSenderCache.Remove(key);
                }
            }
        }

        return (JsonObject)sender.DeepClone();
    }

    /// <summary>私聊消息发送人信息。</summary>
    public static async Task<JsonObject> GetPrivateSenderAsync(long qq)
    {
        return new JsonObject
        {
            ["user_id"] = qq,
            ["nickname"] = await GetFriendNickAsync(qq),
            ["sex"] = "unknown",
            ["age"] = 0,
        };
    }

    /// <summary>通过好友列表查询昵称（带缓存，查不到返回空）。</summary>
    public static async Task<string> GetFriendNickAsync(long qq)
    {
        lock (CacheLock)
        {
            if (UserCache.TryGetValue(qq, out var cached) && DateTimeOffset.Now - cached.Time < CacheTtl)
            {
                return cached.Nick;
            }
        }

        string nick = "";
        try
        {
            var friends = await PluginRuntime.Api.FriendApi.GetFriendInfosAsync();
            var friend = friends?.FirstOrDefault(f => f.QQ == qq);
            if (friend != null)
            {
                nick = friend.Nick ?? "";
            }
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Debug", "事件", $"获取好友信息失败: {ex.Message}");
        }

        lock (CacheLock)
        {
            UserCache[qq] = (DateTimeOffset.Now, nick);
            if (UserCache.Count > 500)
            {
                foreach (var key in UserCache.Where(kv => DateTimeOffset.Now - kv.Value.Time >= CacheTtl).Select(kv => kv.Key).ToList())
                {
                    UserCache.Remove(key);
                }
            }
        }

        return nick;
    }

    private static readonly ConcurrentDictionary<Type, PropertyInfo?> RequestFlagProps = new();

    /// <summary>
    /// 读取请求上下文的 flag。RequestFlag 在 SDK 中是私有属性，
    /// OneBot 的 set_*_request API 又必须携带 flag，因此这里通过反射读取。
    /// </summary>
    private static string RequestFlagOf(object context)
    {
        try
        {
            var prop = RequestFlagProps.GetOrAdd(
                context.GetType(),
                t => t.GetProperty("RequestFlag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
            return prop?.GetValue(context)?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
