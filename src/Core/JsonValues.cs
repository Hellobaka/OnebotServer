using System.Globalization;
using System.Text.Json.Nodes;

namespace OnebotServer.Core;

/// <summary>
/// JsonValue 宽松取值。
/// 注意：JSON 解析出的 JsonElement 与代码内联构造的原始类型 JsonValue 对 TryGetValue&lt;T&gt;
/// 都是“严格类型”语义（int 值取 long 会失败），因此这里逐个探测所有常见原始类型。
/// </summary>
public static class JsonValues
{
    public static bool TryGetLong(JsonNode? node, out long value)
    {
        value = 0;
        if (node is not JsonValue v)
        {
            return false;
        }

        if (v.TryGetValue<long>(out var l))
        {
            value = l;
            return true;
        }

        if (v.TryGetValue<int>(out var i))
        {
            value = i;
            return true;
        }

        if (v.TryGetValue<double>(out var d))
        {
            value = (long)d;
            return true;
        }

        if (v.TryGetValue<decimal>(out var m))
        {
            value = (long)m;
            return true;
        }

        if (v.TryGetValue<bool>(out var b))
        {
            value = b ? 1 : 0;
            return true;
        }

        if (v.TryGetValue<string>(out var s))
        {
            if (long.TryParse(s, out var parsed))
            {
                value = parsed;
                return true;
            }

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                value = (long)parsedDouble;
                return true;
            }
        }

        return false;
    }

    public static bool TryGetBool(JsonNode? node, out bool value)
    {
        value = false;
        if (node is not JsonValue v)
        {
            return false;
        }

        if (v.TryGetValue<bool>(out var b))
        {
            value = b;
            return true;
        }

        if (v.TryGetValue<string>(out var s))
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "true" or "1" or "yes" or "on":
                    value = true;
                    return true;
                case "false" or "0" or "no" or "off":
                    return true;
            }
        }

        if (v.TryGetValue<long>(out var l))
        {
            value = l != 0;
            return true;
        }

        if (v.TryGetValue<int>(out var i))
        {
            value = i != 0;
            return true;
        }

        if (v.TryGetValue<double>(out var d))
        {
            value = d != 0;
            return true;
        }

        return false;
    }

    public static string ToText(JsonNode? node)
    {
        if (node is null)
        {
            return "";
        }

        if (node is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s))
            {
                return s;
            }

            if (v.TryGetValue<bool>(out var b))
            {
                return b ? "true" : "false";
            }

            if (v.TryGetValue<long>(out var l))
            {
                return l.ToString(CultureInfo.InvariantCulture);
            }

            if (v.TryGetValue<int>(out var i))
            {
                return i.ToString(CultureInfo.InvariantCulture);
            }

            if (v.TryGetValue<double>(out var d))
            {
                return d.ToString(CultureInfo.InvariantCulture);
            }

            if (v.TryGetValue<decimal>(out var m))
            {
                return m.ToString(CultureInfo.InvariantCulture);
            }
        }

        return node.ToJsonString();
    }
}
