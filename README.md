# OnebotServer — AMN2 的 OneBot v11 服务插件

基于 [OneBot v11 标准](https://github.com/botuniverse/onebot-11) 为 Another-Mirai-Native2（AMN2）实现的协议服务插件，
让任意 OneBot v11 兼容客户端（NoneBot、koishi、真寻 等）通过标准协议接入 AMN2。

提供三种通信服务（外加标准中的 HTTP POST 事件上报），均可通过配置文件独立开关：

| 服务 | 说明 | 配置节 |
| --- | --- | --- |
| HTTP | OneBot 作为 HTTP 服务端，提供 `/:action` API 调用 | `http` |
| HTTP POST | OneBot 作为 HTTP 客户端上报事件，支持 HMAC-SHA1 签名与快速操作 | `http_post` |
| Websocket-Server（正向 WebSocket） | OneBot 作为 WebSocket 服务端，提供 `/api`、`/event`、`/` 三种接口 | `ws` |
| Websocket-Reverse（反向 WebSocket） | OneBot 作为 WebSocket 客户端主动连接，支持 API / Event / Universal 三种角色 | `ws_reverse` |

## 构建

要求：Windows + .NET SDK 10（`net10.0-windows`）。

```powershell
dotnet build src\OnebotServer\OnebotServer.csproj -c Release
```

构建产物（在 `src\OnebotServer\bin\Release\net10.0-windows\`）：

- `Native_OnebotServer.dll` — 插件本体
- `Native_OnebotServer.json` — 插件清单

## 部署

1. 将上述两个文件复制到 AMN2 框架的 `data\plugins\` 目录；
2. 在框架 UI 中「重载插件」或重启框架，然后启用 **OneBot v11 服务**；
3. 插件数据目录（框架分配，形如 `data\plugins\com.amn2.onebotserver\`）下会自动生成 `config.json`；
4. 框架菜单中提供：
   - **OneBot v11 服务设置...** — 图形化编辑配置并即时应用
   - **重载 OneBot 配置** — 从 config.json 重新加载并重启服务
   - **打开 OneBot 配置目录**

> 提示：开发调试可配合 `Protocol_NoConnection` 协议离线运行。

## 配置文件（config.json）

```json
{
  "http": {
    "enable": true,
    "host": "0.0.0.0",
    "port": 5700,
    "timeout": 0
  },
  "http_post": {
    "enable": true,
    "url": "",
    "timeout": 0,
    "secret": ""
  },
  "ws": {
    "enable": false,
    "host": "0.0.0.0",
    "port": 6700
  },
  "ws_reverse": {
    "enable": true,
    "url": "",
    "api_url": "",
    "event_url": "",
    "use_universal_client": false,
    "reconnect_interval": 3000
  },
  "auth": {
    "access_token": ""
  },
  "event": {
    "message_format": "string"
  },
  "heartbeat": {
    "enable": false,
    "interval": 15000
  },
  "api": {
    "rate_limit_interval": 500
  }
}
```

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `http.enable` | `true` | 是否启用 HTTP 服务 |
| `http.host` | `0.0.0.0` | HTTP 服务器监听的 IP |
| `http.port` | `5700` | HTTP 服务器监听的端口 |
| `http_post.enable` | `true` | 是否启用 HTTP POST 事件上报（`url` 为空时实际上报关闭） |
| `http_post.url` | 空 | 事件上报 URL |
| `http_post.timeout` | `0` | 上报超时时间，单位秒，0 表示不限制 |
| `http_post.secret` | 空 | 签名密钥，非空时附加 `X-Signature: sha1=<hmac-sha1>` |
| `ws.enable` | `false` | 是否启用正向 WebSocket |
| `ws.host` | `0.0.0.0` | WebSocket 服务器监听的 IP |
| `ws.port` | `6700` | WebSocket 服务器监听的端口 |
| `ws_reverse.enable` | `true` | 是否启用反向 WebSocket（无有效 URL 时实际上不连接） |
| `ws_reverse.url` | 空 | API、Event、Universal 共用 URL |
| `ws_reverse.api_url` | 空 | API URL，为空时使用 `url` |
| `ws_reverse.event_url` | 空 | Event URL，为空时使用 `url` |
| `ws_reverse.use_universal_client` | `false` | `true` 时只建一条 Universal 连接（使用 `url`，为空时回退 `api_url`/`event_url`） |
| `ws_reverse.reconnect_interval` | `3000` | 断线重连间隔，单位毫秒 |
| `auth.access_token` | 空 | access token，非空时启用鉴权 |
| `event.message_format` | `string` | 事件中 `message` 字段格式：`string`（CQ 码）或 `array`（消息段数组） |
| `heartbeat.enable` | `false` | 是否产生心跳元事件 |
| `heartbeat.interval` | `15000` | 心跳间隔，单位毫秒 |
| `api.rate_limit_interval` | `500` | `_rate_limited` 调用的排队间隔，单位毫秒 |

### 鉴权

- HTTP / 正向 WebSocket：请求头 `Authorization: Bearer <access_token>`，或 query 参数 `access_token`；
  未提供返回 401，不匹配返回 403（正向 WebSocket 鉴权失败时连接直接断开）。
- 反向 WebSocket：连接请求自动附加 `Authorization`、`X-Self-ID`、`X-Client-Role` 头。
- HTTP POST：`secret` 非空时附带 `X-Signature: sha1=<hex>`，为请求正文的 HMAC-SHA1。

## API 支持情况

调用方式与响应结构（`{status, retcode, data, echo}`）完全遵循标准；
所有 API 均支持 `_async`、`_rate_limited` 后缀（可叠加）。

| API | 支持 | 说明 |
| --- | --- | --- |
| `send_private_msg` / `send_group_msg` / `send_msg` | ✅ | `message` 支持字符串 / 消息段数组 / 单消息段；`auto_escape` 按纯文本发送 |
| `delete_msg` | ✅ | |
| `get_msg` | ✅ | 优先使用消息 ID 映射缓存，未命中时在全部群/好友会话中查找 |
| `send_like` | ✅ | |
| `set_group_kick` / `set_group_ban` / `set_group_whole_ban` | ✅ | |
| `set_group_admin` / `set_group_card` / `set_group_special_title` | ✅ | `special_title` 的 `duration` 参数底层暂不支持 |
| `set_group_leave` | ✅ | 群主退出即解散，`is_dismiss` 无独立开关 |
| `set_friend_add_request` / `set_group_add_request` | ✅ | `add` / `invite` 子类型均支持 |
| `get_login_info` / `get_status` / `get_version_info` | ✅ | |
| `get_stranger_info` | ✅ | 依次检索好友列表与各群成员，未查到时返回空昵称 |
| `get_friend_list` / `get_group_info` / `get_group_list` | ✅ | |
| `get_group_member_info` / `get_group_member_list` | ✅ | |
| `can_send_image` / `can_send_record` | ✅ | 恒为 `yes: true` |
| `get_image` | ✅ | 按文件名或图片 hash 查找 `data\image` |
| `get_record` | ✅ | 按文件名查找 `data\record`；`out_format` 转换需 ffmpeg，暂不支持 |
| `set_restart` | ✅ | 以重载插件的方式重启 OneBot 服务，返回 `status: async` |
| `clean_cache` | ✅ | 空实现（成功） |
| `send_group_forward_msg` / `send_private_forward_msg` | 🧩 扩展 | 标准之外的合并转发发送，节点支持 `id` 或 `user_id/nickname/content` |
| `.handle_quick_operation` | ✅ | 隐藏 API |
| `get_forward_msg`、`get_group_honor_info`、`get_cookies`、`get_csrf_token`、`get_credentials`、`set_group_name`、`set_group_anonymous`、`set_group_anonymous_ban` | ❌ | 底层框架无对应能力，返回 `failed`（retcode 200） |

返回码：`0` 成功、`1` 异步、`100` 参数错误、`200` 操作失败、`1400` 请求格式不正确、`1404` API 不存在。

## 事件支持情况

消息、通知、请求、元事件均按标准字段上报。生命周期 `enable`/`disable` 仅经 HTTP POST 上报，
`connect` 仅在正向/反向 WebSocket 连接建立时推送，与标准一致。

| AMN2 事件 | OneBot v11 事件 |
| --- | --- |
| 群消息 | `message` / `group`（`sub_type: normal`，`anonymous` 恒为 null） |
| 私聊消息 | `message` / `private`（`sub_type: friend`） |
| 群成员入群 | `notice` / `group_increase`（`approve` 或 `invite`） |
| 群成员退群/被踢 | `notice` / `group_decrease`（`leave` / `kick` / `kick_me`） |
| 群成员禁言/解禁 | `notice` / `group_ban`（`ban` / `lift_ban`，含 `duration`） |
| 群管理员变动 | `notice` / `group_admin`（`set` / `unset`） |
| 全员禁言/解除 | `notice` / `group_ban`（🧩 扩展 `sub_type: whole_ban` / `whole_lift_ban`，`user_id: 0`） |
| 好友添加 | `notice` / `friend_add` |
| 群文件上传 | `notice` / `group_upload` |
| 加好友请求 | `request` / `friend` |
| 加群请求/邀请 | `request` / `group`（`add` / `invite`） |
| — | `meta_event` / `lifecycle`（`enable`/`disable`/`connect`） |
| 心跳 | `meta_event` / `heartbeat`（需 `heartbeat.enable`） |

> 说明：OneBot v11 标准未定义全员禁言事件，这里使用 `notice/group_ban` + `whole_ban`/`whole_lift_ban` 子类型扩展上报。
> AMN2 不提供撤回通知、戳一戳等事件接口，因此 `group_recall`、`friend_recall`、`notify` 类事件暂不产生。

## 快速操作

HTTP POST 上报的响应正文（或 `.handle_quick_operation` 的 `operation`）支持：

| 字段 | 适用事件 | 说明 |
| --- | --- | --- |
| `reply`、`auto_escape`、`at_sender` | 消息事件 | 快速回复；群消息默认在回复开头 at 发送者（`at_sender` 默认 `true`） |
| `delete` | 消息事件 | 撤回该条消息 |
| `kick` | 群消息 | 踢出发送者（不拒绝后续加群请求） |
| `ban`、`ban_duration` | 群消息 | 禁言发送者，默认 30 分钟 |
| `approve`、`remark` | 加好友请求 | 同意/拒绝，同意后备注 |
| `approve`、`reason` | 加群请求/邀请 | 同意/拒绝，拒绝理由 |

所有字段均为可选，仅在字段存在时触发；`approve` 不填则不处理请求。

## 消息与媒体

- 消息格式转换遵循标准的 CQ 码转义规则（文本 `& [ ]`，参数值额外转义 `,`）；
- 发送时 `file` 参数支持：媒体目录内的相对文件名、`base64://`、`http(s)://`、`file:///` 绝对路径，
  后三者会自动下载/复制到框架的 `data\image`（图片/视频）与 `data\record`（语音）后再发送；
- 消息段类型按标准透传（`text`、`face`、`image`、`record`、`video`、`at`、`reply`、`json`、`xml`、`dice`、`rps` 等），
  其中底层框架未实现的类型（如 `music`、`share`、`location`）可能被忽略。

## 常见问题

- **HTTP / 正向 WS 监听 `0.0.0.0` 启动失败**：`HttpListener` 监听非本机地址需要管理员权限。
  插件会在这种情况下**自动回退为仅监听 `127.0.0.1`** 并输出告警日志，保证本机可用；
  如需局域网访问，请以管理员运行框架，或执行 `netsh http add urlacl=http://+:5700/ user=Everyone`（端口按实际配置）后重试；
  仅监听 `127.0.0.1` 无需特权。
- **get_msg 返回“消息不存在”**：消息 ID 映射缓存最多保留最近 2000 条，更早的消息会逐会话回查，仍未找到则失败。
- **客户端收不到事件**：检查对应服务的 `enable`、`url`；事件先经 HTTP POST 上报（等待其响应并执行快速操作），
  再推送给正向/反向 WebSocket 的 Event 与 Universal 连接。
