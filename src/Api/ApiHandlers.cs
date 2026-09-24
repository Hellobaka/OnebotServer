using Another_Mirai_Native.Abstractions.Enums;
using Another_Mirai_Native.Abstractions.Models;
using OnebotServer.Core;
using System.Reflection;
using System.Text.Json.Nodes;

namespace OnebotServer.Api;

/// <summary>API 执行结果；ForceAsync 用于 set_restart 这类本身即异步的接口。</summary>
public readonly record struct ApiResult(JsonNode? Data, bool ForceAsync = false);

/// <summary>
/// OneBot v11 公开 API 到 AMN2 接口的映射。
/// 不被底层框架支持的接口会返回 failed（retcode 200），未知接口抛出 ApiNotFoundException。
/// </summary>
public static class ApiHandlers
{
    private static Another_Mirai_Native.Abstractions.Services.IPluginApi Api => PluginRuntime.Api;

    public static async Task<ApiResult> ExecuteAsync(string action, ApiParams p)
    {
        switch (action)
        {
            // ---------- 消息 ----------
            case "send_private_msg":
                return new ApiResult(await SendToAsync(false, p.Long("user_id", required: true), p));
            case "send_group_msg":
                return new ApiResult(await SendToAsync(true, p.Long("group_id", required: true), p));
            case "send_msg":
                {
                    string messageType = p.Str("message_type");
                    long groupId = p.Long("group_id");
                    long userId = p.Long("user_id");
                    bool isGroup = messageType == "group" || (messageType != "private" && groupId != 0);
                    return new ApiResult(await SendToAsync(isGroup, isGroup ? groupId : userId, p));
                }

            case "delete_msg":
                {
                    bool ok = await Api.MessageApi.DeleteMessageAsync(p.Long("message_id", required: true));
                    return BoolResult(ok, "撤回消息失败");
                }

            case "get_msg":
                return new ApiResult(await GetMsgAsync(p.Int("message_id", required: true)));

            case "get_forward_msg":
                throw new ApiFailedException("当前实现不支持获取合并转发消息");

            // 扩展接口：发送合并转发消息
            case "send_group_forward_msg":
                {
                    var nodes = await BuildForwardNodesAsync(p.Node("messages"));
                    int id = await Api.MessageApi.SendGroupForwardMessageAsync(p.Long("group_id", required: true), nodes);
                    PluginRuntime.RememberMessage(id, p.Long("group_id"), true);
                    return new ApiResult(new JsonObject { ["message_id"] = id });
                }

            case "send_private_forward_msg":
                {
                    var nodes = await BuildForwardNodesAsync(p.Node("messages"));
                    int id = await Api.MessageApi.SendPrivateForwardMessageAsync(p.Long("user_id", required: true), nodes);
                    PluginRuntime.RememberMessage(id, p.Long("user_id"), false);
                    return new ApiResult(new JsonObject { ["message_id"] = id });
                }

            case "send_like":
                {
                    long times = p.Has("times") ? p.Long("times") : 1;
                    bool ok = await Api.FriendApi.SendPraiseAsync(p.Long("user_id", required: true), (int)times);
                    return BoolResult(ok, "发送好友赞失败");
                }

            // ---------- 群操作 ----------
            case "set_group_kick":
                {
                    bool ok = await Api.GroupApi.KickAsync(
                        p.Long("group_id", required: true),
                        p.Long("user_id", required: true),
                        p.Bool("reject_add_request"));
                    return BoolResult(ok, "群组踢人失败");
                }

            case "set_group_ban":
                {
                    long duration = p.Has("duration") ? p.Long("duration") : 30 * 60;
                    bool ok = await Api.GroupApi.BanMemberAsync(
                        p.Long("group_id", required: true),
                        p.Long("user_id", required: true),
                        duration);
                    return BoolResult(ok, "群组禁言失败");
                }

            case "set_group_anonymous_ban":
                throw new ApiFailedException("当前实现不支持匿名用户禁言");

            case "set_group_whole_ban":
                {
                    bool ok = await Api.GroupApi.BanGroupAsync(p.Long("group_id", required: true), p.Bool("enable", true));
                    return BoolResult(ok, "全员禁言失败");
                }

            case "set_group_admin":
                {
                    bool ok = await Api.GroupApi.SetAdminAsync(
                        p.Long("group_id", required: true),
                        p.Long("user_id", required: true),
                        p.Bool("enable", true));
                    return BoolResult(ok, "设置管理员失败");
                }

            case "set_group_anonymous":
                throw new ApiFailedException("当前实现不支持群匿名设置");

            case "set_group_card":
                {
                    bool ok = await Api.GroupApi.SetMemberCardAsync(
                        p.Long("group_id", required: true),
                        p.Long("user_id", required: true),
                        p.Str("card"));
                    return BoolResult(ok, "设置群名片失败");
                }

            case "set_group_name":
                throw new ApiFailedException("当前实现不支持设置群名");

            case "set_group_leave":
                {
                    // 框架中群主退出即解散群，is_dismiss 无独立开关
                    bool ok = await Api.GroupApi.LeaveAsync(p.Long("group_id", required: true));
                    return BoolResult(ok, "退出群组失败");
                }

            case "set_group_special_title":
                {
                    // duration 参数框架暂不支持
                    bool ok = await Api.GroupApi.SetMemberTitleAsync(
                        p.Long("group_id", required: true),
                        p.Long("user_id", required: true),
                        p.Str("special_title"));
                    return BoolResult(ok, "设置专属头衔失败");
                }

            // ---------- 请求处理 ----------
            case "set_friend_add_request":
                {
                    bool ok = await Api.FriendApi.SetFriendAddRequestAsync(
                        p.Str("flag", required: true),
                        p.Bool("approve", true),
                        p.Str("remark"));
                    return BoolResult(ok, "处理加好友请求失败");
                }

            case "set_group_add_request":
                {
                    string subType = p.Has("sub_type") ? p.Str("sub_type") : p.Str("type");
                    bool approve = p.Bool("approve", true);
                    string flag = p.Str("flag", required: true);
                    string reason = p.Str("reason");
                    bool ok = subType == "invite"
                        ? await Api.GroupApi.SetGroupInviteRequestAsync(flag, approve, reason)
                        : await Api.GroupApi.SetGroupAddRequestAsync(flag, approve, reason);
                    return BoolResult(ok, "处理加群请求失败");
                }

            // ---------- 信息查询 ----------
            case "get_login_info":
                return new ApiResult(new JsonObject
                {
                    ["user_id"] = Api.AppApi.GetLoginQQ(),
                    ["nickname"] = Api.AppApi.GetLoginQQNick(),
                });

            case "get_stranger_info":
                return new ApiResult(await GetStrangerInfoAsync(p.Long("user_id", required: true)));

            case "get_friend_list":
                {
                    var friends = await Api.FriendApi.GetFriendInfosAsync() ?? new List<FriendInfo>();
                    var arr = new JsonArray();
                    foreach (var f in friends)
                    {
                        arr.Add(new JsonObject
                        {
                            ["user_id"] = f.QQ,
                            ["nickname"] = f.Nick ?? "",
                            ["remark"] = f.Postscript ?? "",
                        });
                    }

                    return new ApiResult(arr);
                }

            case "get_group_info":
                {
                    var info = await Api.GroupApi.GetGroupInfoAsync(p.Long("group_id", required: true))
                        ?? throw new ApiFailedException("群信息不存在");
                    if (info.Group == 0)
                    {
                        // 框架对不存在的群返回全空对象
                        throw new ApiFailedException("群信息不存在");
                    }

                    return new ApiResult(GroupJson(info));
                }

            case "get_group_list":
                {
                    var groups = await Api.GroupApi.GetGroupListAsync() ?? new List<GroupInfo>();
                    var arr = new JsonArray();
                    foreach (var g in groups)
                    {
                        arr.Add(GroupJson(g));
                    }

                    return new ApiResult(arr);
                }

            case "get_group_member_info":
                {
                    var info = await Api.GroupApi.GetGroupMemberInfoAsync(
                        p.Long("group_id", required: true),
                        p.Long("user_id", required: true))
                        ?? throw new ApiFailedException("群成员信息不存在");
                    if (info.QQ == 0)
                    {
                        // 框架对不存在的成员返回全空对象
                        throw new ApiFailedException("群成员信息不存在");
                    }

                    return new ApiResult(MemberJson(info));
                }

            case "get_group_member_list":
                {
                    var members = await Api.GroupApi.GetGroupMembersAsync(p.Long("group_id", required: true))
                        ?? new List<GroupMemberInfo>();
                    var arr = new JsonArray();
                    foreach (var m in members)
                    {
                        arr.Add(MemberJson(m));
                    }

                    return new ApiResult(arr);
                }

            case "get_group_honor_info":
                throw new ApiFailedException("当前实现不支持获取群荣誉信息");

            case "get_cookies":
            case "get_csrf_token":
            case "get_credentials":
                throw new ApiFailedException("当前实现不支持获取 QQ 接口凭证");

            case "get_record":
                return new ApiResult(GetRecordPath(p.Str("file", required: true), p.Str("out_format")));

            case "get_image":
                return new ApiResult(await GetImagePathAsync(p.Str("file", required: true)));

            case "can_send_image":
            case "can_send_record":
                return new ApiResult(new JsonObject { ["yes"] = true });

            case "get_status":
                return new ApiResult(EventFactory.StatusObject());

            case "get_version_info":
                string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
                return new ApiResult(new JsonObject
                {
                    ["app_name"] = "OnebotServer",
                    ["app_version"] = version,
                    ["protocol_version"] = "v11",
                    ["runtime"] = "Another-Mirai-Native2",
                });

            case "set_restart":
                {
                    int delay = p.Int("delay");
                    PluginRuntime.Log("Info", "API", $"{delay}ms 后重载插件以重启 OneBot 服务");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(delay);
                        try
                        {
                            Api.AppApi.ReloadPlugin();
                        }
                        catch (Exception ex)
                        {
                            PluginRuntime.Log("Error", "API", $"重载插件失败: {ex.Message}");
                        }
                    });
                    return new ApiResult(null, ForceAsync: true);
                }

            case "clean_cache":
                return new ApiResult(null);

            // ---------- 隐藏 API ----------
            case ".handle_quick_operation":
                {
                    var context = p.Node("context") as JsonObject ?? throw new ApiParamException("缺少参数 context");
                    var operation = p.Node("operation") as JsonObject ?? throw new ApiParamException("缺少参数 operation");
                    await QuickOperation.ExecuteAsync(context, operation);
                    return new ApiResult(null);
                }

            default:
                throw new ApiNotFoundException(action);
        }
    }

    // ---------- 消息发送 ----------

    private static async Task<JsonNode> SendToAsync(bool isGroup, long target, ApiParams p)
    {
        var message = p.Node("message") ?? throw new ApiParamException("缺少参数 message");
        bool autoEscape = p.Bool("auto_escape");
        string cq = await MessageConverter.ToCqStringAsync(message, autoEscape);
        return await SendCqAsync(isGroup, target, cq);
    }

    private static async Task<JsonNode> SendCqAsync(bool isGroup, long target, string cq)
    {
        int id = isGroup
            ? await Api.MessageApi.SendGroupMessageAsync(target, cq)
            : await Api.MessageApi.SendPrivateMessageAsync(target, cq);
        PluginRuntime.RememberMessage(id, target, isGroup);
        return new JsonObject { ["message_id"] = id };
    }

    private static async Task<string[]> BuildForwardNodesAsync(JsonNode? messages)
    {
        if (messages is not JsonArray arr)
        {
            throw new ApiParamException("缺少参数 messages（消息段数组）");
        }

        var nodes = new List<string>();
        foreach (var item in arr)
        {
            if (item is not JsonObject node)
            {
                continue;
            }

            var data = node["data"] as JsonObject ?? new JsonObject();
            if (data["id"] != null)
            {
                nodes.Add(CQString.Build("node", new Dictionary<string, string>
                {
                    ["id"] = NodeString(data["id"]),
                }));
                continue;
            }

            string content = await MessageConverter.ToCqStringAsync(data["content"], false);
            nodes.Add(CQString.Build("node", new Dictionary<string, string>
            {
                ["user_id"] = NodeString(data["user_id"]),
                ["nickname"] = NodeString(data["nickname"]),
                ["content"] = content,
            }));
        }

        return nodes.ToArray();
    }

    private static string NodeString(JsonNode? node) => JsonValues.ToText(node);

    // ---------- 消息查询 ----------

    private static async Task<JsonNode> GetMsgAsync(int messageId)
    {
        var history = await FindMessageAsync(messageId)
            ?? throw new ApiFailedException("消息不存在");

        bool isGroup = history.Type == ChatHistoryType.Group;
        JsonObject sender = isGroup
            ? await EventFactory.GetGroupSenderAsync(history.ParentID, history.SenderID)
            : await EventFactory.GetPrivateSenderAsync(history.SenderID);

        return new JsonObject
        {
            ["time"] = EventFactory.UnixSeconds(history.Time),
            ["message_type"] = isGroup ? "group" : "private",
            ["message_id"] = history.MessageId,
            ["real_id"] = history.MessageId,
            ["sender"] = sender,
            ["message"] = MessageConverter.BuildMessageField(history.Message),
        };
    }

    private static async Task<ChatHistory?> FindMessageAsync(int messageId)
    {
        if (PluginRuntime.MessageOrigins.TryGetValue(messageId, out var origin))
        {
            var hit = await Api.MessageApi.GetChatHistoryByIdAsync(origin.ParentId, origin.IsGroup, messageId);
            if (HasContent(hit))
            {
                return hit;
            }
        }

        // 缓存未命中时在全部群 / 好友会话中查找
        try
        {
            var groups = await Api.GroupApi.GetGroupListAsync();
            foreach (var g in groups ?? Enumerable.Empty<GroupInfo>())
            {
                var hit = await Api.MessageApi.GetChatHistoryByIdAsync(g.Group, true, messageId);
                if (HasContent(hit))
                {
                    PluginRuntime.RememberMessage(messageId, g.Group, true);
                    return hit;
                }
            }
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Debug", "API", $"get_msg 群查找失败: {ex.Message}");
        }

        try
        {
            var friends = await Api.FriendApi.GetFriendInfosAsync();
            foreach (var f in friends ?? Enumerable.Empty<FriendInfo>())
            {
                var hit = await Api.MessageApi.GetChatHistoryByIdAsync(f.QQ, false, messageId);
                if (HasContent(hit))
                {
                    PluginRuntime.RememberMessage(messageId, f.QQ, false);
                    return hit;
                }
            }
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Debug", "API", $"get_msg 好友查找失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 框架对不存在的消息可能返回一条全空的 ChatHistory 而不是 null，
    /// 这里把“所有关键字段均为空”的记录视作未找到。
    /// </summary>
    private static bool HasContent(Another_Mirai_Native.Abstractions.Models.ChatHistory? history)
        => history != null
            && (history.MessageId != 0 || history.SenderID != 0 || history.ParentID != 0 || !string.IsNullOrEmpty(history.Message));

    // ---------- 信息映射 ----------

    private static async Task<JsonNode> GetStrangerInfoAsync(long userId)
    {
        try
        {
            var friends = await Api.FriendApi.GetFriendInfosAsync();
            var friend = friends?.FirstOrDefault(f => f.QQ == userId);
            if (friend != null)
            {
                return new JsonObject
                {
                    ["user_id"] = friend.QQ,
                    ["nickname"] = friend.Nick ?? "",
                    ["sex"] = "unknown",
                    ["age"] = 0,
                };
            }
        }
        catch
        {
        }

        try
        {
            var groups = await Api.GroupApi.GetGroupListAsync();
            foreach (var g in groups ?? Enumerable.Empty<GroupInfo>())
            {
                var member = await Api.GroupApi.GetGroupMemberInfoAsync(g.Group, userId);
                if (member != null && member.QQ != 0)
                {
                    return new JsonObject
                    {
                        ["user_id"] = member.QQ,
                        ["nickname"] = member.Nick ?? "",
                        ["sex"] = EventFactory.SexString(member.Sex),
                        ["age"] = member.Age,
                    };
                }
            }
        }
        catch
        {
        }

        // 尽最大努力提供；未查到时返回空昵称
        return new JsonObject
        {
            ["user_id"] = userId,
            ["nickname"] = "",
            ["sex"] = "unknown",
            ["age"] = 0,
        };
    }

    private static JsonObject GroupJson(GroupInfo info) => new()
    {
        ["group_id"] = info.Group,
        ["group_name"] = info.Name ?? "",
        ["member_count"] = info.CurrentMemberCount,
        ["max_member_count"] = info.MaxMemberCount,
    };

    private static JsonObject MemberJson(GroupMemberInfo info)
    {
        long titleExpire = 0;
        if (info.ExclusiveTitleExpirationTime is { } expire)
        {
            titleExpire = EventFactory.UnixSeconds(expire);
        }

        return new JsonObject
        {
            ["group_id"] = info.Group,
            ["user_id"] = info.QQ,
            ["nickname"] = info.Nick ?? "",
            ["card"] = info.Card ?? "",
            ["sex"] = EventFactory.SexString(info.Sex),
            ["age"] = info.Age,
            ["area"] = info.Area ?? "",
            ["join_time"] = EventFactory.UnixSeconds(info.JoinGroupDateTime),
            ["last_sent_time"] = EventFactory.UnixSeconds(info.LastSpeakDateTime),
            ["level"] = info.Level ?? "",
            ["role"] = EventFactory.RoleString(info.MemberType),
            ["unfriendly"] = info.IsBadRecord,
            ["title"] = info.ExclusiveTitle ?? "",
            ["title_expire_time"] = titleExpire,
            ["card_changeable"] = info.IsAllowEditorCard,
        };
    }

    // ---------- 媒体 ----------

    private static JsonNode GetRecordPath(string file, string outFormat)
    {
        string path = FindMediaFile(PluginRuntime.RecordDir, file) ?? file;
        if (!File.Exists(path))
        {
            throw new ApiFailedException("语音文件不存在");
        }

        string currentExt = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (!string.IsNullOrEmpty(outFormat) && !string.Equals(outFormat, currentExt, StringComparison.OrdinalIgnoreCase))
        {
            // 格式转换依赖 ffmpeg，当前实现不支持
            throw new ApiFailedException($"当前实现不支持将语音转换为 {outFormat} 格式");
        }

        return new JsonObject { ["file"] = path };
    }

    private static async Task<JsonNode> GetImagePathAsync(string file)
    {
        string? path = FindMediaFile(PluginRuntime.ImageDir, file);
        if (path == null)
        {
            // 收到的图片可通过 hash 查询
            foreach (var candidate in new[] { file, Path.GetFileNameWithoutExtension(file) })
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                var (ok, found) = await Api.MessageApi.TryGetImageByHashAsync(candidate);
                if (ok && !string.IsNullOrEmpty(found))
                {
                    path = found;
                    break;
                }
            }
        }

        if (path == null)
        {
            throw new ApiFailedException("图片文件不存在");
        }

        return new JsonObject { ["file"] = path };
    }

    private static string? FindMediaFile(string dir, string file)
    {
        if (Path.IsPathRooted(file))
        {
            return File.Exists(file) ? file : null;
        }

        string direct = Path.Combine(dir, file);
        if (File.Exists(direct))
        {
            return direct;
        }

        try
        {
            if (Directory.Exists(dir))
            {
                return Directory.EnumerateFiles(dir, Path.GetFileName(file), SearchOption.AllDirectories).FirstOrDefault();
            }
        }
        catch
        {
        }

        return null;
    }

    private static ApiResult BoolResult(bool ok, string error)
        => ok ? new ApiResult(null) : throw new ApiFailedException(error);
}
