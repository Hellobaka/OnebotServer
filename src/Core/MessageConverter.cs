using System.Text;
using System.Text.Json.Nodes;

namespace OnebotServer.Core;

/// <summary>
/// OneBot 消息（string / array / 单消息段）与 CQ 码字符串之间的相互转换，
/// 并负责把 base64 / 网络 URL / file:// 等形式的媒体文件落到框架媒体目录。
/// </summary>
public static class MessageConverter
{
    /// <summary>将 CQ 码字符串转换为 OneBot 消息段数组。</summary>
    public static JsonArray ToArrayFormat(string? rawCq)
    {
        var arr = new JsonArray();
        foreach (var seg in CQString.Parse(rawCq))
        {
            var data = new JsonObject();
            foreach (var kv in seg.Data)
            {
                data[kv.Key] = kv.Value;
            }

            arr.Add(new JsonObject
            {
                ["type"] = seg.Type,
                ["data"] = data,
            });
        }

        return arr;
    }

    /// <summary>
    /// 将 OneBot 的 message 参数（字符串 / 消息段数组 / 单个消息段对象）转换为 CQ 码字符串。
    /// autoEscape 为 true 时按纯文本发送（不解析 CQ 码），仅对字符串入参有效。
    /// </summary>
    public static async Task<string> ToCqStringAsync(JsonNode? message, bool autoEscape)
    {
        switch (message)
        {
            case null:
                return "";
            case JsonValue value when value.TryGetValue<string>(out var text):
                return autoEscape ? CQString.EscapeText(text) : text;
            case JsonArray arr:
                {
                    var sb = new StringBuilder();
                    foreach (var item in arr)
                    {
                        sb.Append(await SegmentToCqAsync(item as JsonObject));
                    }

                    return sb.ToString();
                }
            case JsonObject obj:
                return await SegmentToCqAsync(obj);
            default:
                return CQString.EscapeText(message.ToJsonString());
        }
    }

    /// <summary>按当前配置的 event.message_format 生成事件中的 message 字段。</summary>
    public static JsonNode BuildMessageField(string? rawCq)
    {
        return PluginRuntime.Config.Event.MessageFormat.Equals("array", StringComparison.OrdinalIgnoreCase)
            ? ToArrayFormat(rawCq)
            : rawCq ?? "";
    }

    private static async Task<string> SegmentToCqAsync(JsonObject? seg)
    {
        if (seg == null)
        {
            return "";
        }

        string type = "text";
        if (seg["type"] != null)
        {
            type = JsonValues.ToText(seg["type"]);
        }

        var data = new Dictionary<string, string>();
        if (seg["data"] is JsonObject d)
        {
            foreach (var kv in d)
            {
                if (kv.Value == null)
                {
                    data[kv.Key] = "";
                }
                else if (kv.Value is JsonArray)
                {
                    // 嵌套消息（如合并转发自定义节点的 content）
                    data[kv.Key] = await ToCqStringAsync(kv.Value, false);
                }
                else
                {
                    data[kv.Key] = JsonValues.ToText(kv.Value);
                }
            }
        }

        if (type == "text")
        {
            data.TryGetValue("text", out var text);
            return CQString.EscapeText(text ?? "");
        }

        // 媒体文件：base64 / URL / file:// / 绝对路径统一落到框架媒体目录后按相对路径引用
        if (data.TryGetValue("file", out var file) && !string.IsNullOrEmpty(file))
        {
            bool isRecord = type is "record" or "voice";
            bool isVideo = type == "video";
            data["file"] = await MediaStore.ResolveAsync(file, isRecord, isVideo);

            // 下载控制参数由本插件处理完毕，不再下传
            data.Remove("cache");
            data.Remove("proxy");
            data.Remove("timeout");
        }

        return CQString.Build(type, data);
    }
}

/// <summary>把外部媒体资源保存到框架的 data\image / data\record 目录。</summary>
public static class MediaStore
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// 解析 file 参数：
    /// base64://、http(s)://、file:// 与绝对路径都会复制/下载到媒体目录并返回相对文件名，
    /// 其余形式视为媒体目录下的相对文件名原样返回。
    /// </summary>
    public static async Task<string> ResolveAsync(string file, bool isRecord, bool isVideo = false)
    {
        try
        {
            string dir = isRecord ? PluginRuntime.RecordDir : PluginRuntime.ImageDir;
            string fallbackExt = isRecord ? ".amr" : isVideo ? ".mp4" : ".png";

            if (file.StartsWith("base64://", StringComparison.OrdinalIgnoreCase))
            {
                byte[] bytes = Convert.FromBase64String(file["base64://".Length..]);
                return Save(dir, bytes, SniffExtension(bytes, fallbackExt));
            }

            if (file.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                file.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                byte[] bytes = await Http.GetByteArrayAsync(file);
                string ext = KnownExtension(Path.GetExtension(new Uri(file).AbsolutePath)) is { } known
                    ? known
                    : SniffExtension(bytes, fallbackExt);
                return Save(dir, bytes, ext);
            }

            string localPath;
            if (file.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                localPath = new Uri(file).LocalPath;
            }
            else if (Path.IsPathRooted(file))
            {
                localPath = file;
            }
            else
            {
                return file;
            }

            if (File.Exists(localPath))
            {
                byte[] bytes = await File.ReadAllBytesAsync(localPath);
                string ext = KnownExtension(Path.GetExtension(localPath)) is { } known2
                    ? known2
                    : SniffExtension(bytes, fallbackExt);
                return Save(dir, bytes, ext);
            }

            return file;
        }
        catch
        {
            return file;
        }
    }

    private static string Save(string dir, byte[] bytes, string ext)
    {
        Directory.CreateDirectory(dir);
        string name = $"{Guid.NewGuid():N}{ext}";
        File.WriteAllBytes(Path.Combine(dir, name), bytes);
        return name;
    }

    private static string? KnownExtension(string? ext)
    {
        if (string.IsNullOrEmpty(ext))
        {
            return null;
        }

        return ext.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" => ext.ToLowerInvariant(),
            ".amr" or ".silk" or ".mp3" or ".wav" or ".m4a" or ".ogg" or ".flac" or ".wma" or ".spx" => ext.ToLowerInvariant(),
            ".mp4" => ".mp4",
            _ => null,
        };
    }

    private static string SniffExtension(byte[] b, string fallback)
    {
        if (b.Length >= 4)
        {
            if (b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            {
                return ".jpg";
            }

            if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            {
                return ".png";
            }

            if (b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F')
            {
                return ".gif";
            }

            if (b[0] == (byte)'B' && b[1] == (byte)'M')
            {
                return ".bmp";
            }

            if (b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F')
            {
                return b.Length >= 12 && b[8] == (byte)'W' && b[9] == (byte)'A' ? ".wav" : ".webp";
            }

            if (b.Length >= 12 && b[4] == (byte)'f' && b[5] == (byte)'t' && b[6] == (byte)'y' && b[7] == (byte)'p')
            {
                return ".mp4";
            }

            if (b.Length >= 3 && b[0] == (byte)'#' && b[1] == (byte)'!' && b[2] == (byte)'A')
            {
                return ".amr";
            }

            if (b.Length >= 3 && b[0] == (byte)'#' && b[1] == (byte)'!' && b[2] == (byte)'S')
            {
                return ".silk";
            }

            if (b[0] == (byte)'I' && b[1] == (byte)'D' && b[2] == (byte)'3')
            {
                return ".mp3";
            }

            if (b[0] == 0xFF && (b[1] & 0xE0) == 0xE0)
            {
                return ".mp3";
            }
        }

        return fallback;
    }
}
