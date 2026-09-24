using System.Text;

namespace OnebotServer.Core;

/// <summary>一个消息段（CQ 码的数组格式表示）。</summary>
public sealed class CQSegment
{
    public string Type { get; init; } = "";

    /// <summary>参数表，值为已还原（未转义）的真实值。</summary>
    public Dictionary<string, string> Data { get; init; } = new();

    public bool IsText => Type == "text";

    public string Text => Data.TryGetValue("text", out var t) ? t : "";
}

/// <summary>
/// CQ 码字符串解析与构造。转义规则遵循 OneBot v11「字符串格式」：
/// 文本转义 &amp; &#91; &#93;，参数值额外转义逗号 &#44;。
/// </summary>
public static class CQString
{
    public static string EscapeText(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        return s.Replace("&", "&amp;").Replace("[", "&#91;").Replace("]", "&#93;");
    }

    public static string EscapeParam(string s)
    {
        return EscapeText(s).Replace(",", "&#44;");
    }

    public static string UnescapeText(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        return s.Replace("&#91;", "[").Replace("&#93;", "]").Replace("&amp;", "&");
    }

    public static string UnescapeParam(string s)
    {
        return s.Replace("&#91;", "[").Replace("&#93;", "]").Replace("&#44;", ",").Replace("&amp;", "&");
    }

    /// <summary>将 CQ 码字符串解析为消息段列表。</summary>
    public static List<CQSegment> Parse(string? raw)
    {
        var result = new List<CQSegment>();
        if (string.IsNullOrEmpty(raw))
        {
            return result;
        }

        var text = new StringBuilder();
        int i = 0;
        while (i < raw!.Length)
        {
            if (raw[i] == '[' && i + 4 <= raw.Length && string.CompareOrdinal(raw, i, "[CQ:", 0, 4) == 0)
            {
                int end = raw.IndexOf(']', i + 4);
                if (end < 0)
                {
                    // 没有闭合的 CQ 码按纯文本处理
                    text.Append(raw[i]);
                    i++;
                    continue;
                }

                if (text.Length > 0)
                {
                    result.Add(new CQSegment { Type = "text", Data = new Dictionary<string, string> { ["text"] = UnescapeText(text.ToString()) } });
                    text.Clear();
                }

                string inner = raw.Substring(i + 4, end - i - 4);
                string type;
                string paramPart;
                int comma = inner.IndexOf(',');
                if (comma < 0)
                {
                    type = inner;
                    paramPart = "";
                }
                else
                {
                    type = inner[..comma];
                    paramPart = inner[(comma + 1)..];
                }

                var data = new Dictionary<string, string>();
                if (paramPart.Length > 0)
                {
                    foreach (var pair in paramPart.Split(','))
                    {
                        if (pair.Length == 0)
                        {
                            continue;
                        }

                        int eq = pair.IndexOf('=');
                        if (eq <= 0)
                        {
                            data[UnescapeParam(pair)] = "";
                        }
                        else
                        {
                            data[UnescapeParam(pair[..eq])] = UnescapeParam(pair[(eq + 1)..]);
                        }
                    }
                }

                result.Add(new CQSegment { Type = type, Data = data });
                i = end + 1;
            }
            else
            {
                text.Append(raw[i]);
                i++;
            }
        }

        if (text.Length > 0)
        {
            result.Add(new CQSegment { Type = "text", Data = new Dictionary<string, string> { ["text"] = UnescapeText(text.ToString()) } });
        }

        return result;
    }

    /// <summary>构造单个 CQ 码。参数值会被转义。</summary>
    public static string Build(string type, IEnumerable<KeyValuePair<string, string>>? data)
    {
        var sb = new StringBuilder("[CQ:").Append(type);
        if (data != null)
        {
            foreach (var kv in data)
            {
                sb.Append(',').Append(kv.Key).Append('=').Append(EscapeParam(kv.Value ?? ""));
            }
        }

        return sb.Append(']').ToString();
    }

    /// <summary>将消息段列表序列化为 CQ 码字符串。</summary>
    public static string Join(IEnumerable<CQSegment> segments)
    {
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            sb.Append(ToCode(seg));
        }

        return sb.ToString();
    }

    public static string ToCode(CQSegment seg)
    {
        return seg.IsText ? EscapeText(seg.Text) : Build(seg.Type, seg.Data);
    }
}
