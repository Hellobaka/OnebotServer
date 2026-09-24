using System.Text.Json.Nodes;

namespace OnebotServer.Api;

/// <summary>API 参数缺失或类型不正确。</summary>
public sealed class ApiParamException : Exception
{
    public ApiParamException(string message)
        : base(message)
    {
    }
}

/// <summary>API 不存在。</summary>
public sealed class ApiNotFoundException : Exception
{
    public ApiNotFoundException(string action)
        : base($"未知的 API: {action}")
    {
    }
}

/// <summary>API 存在但本次操作执行失败（或该实现不支持此 API）。</summary>
public sealed class ApiFailedException : Exception
{
    public ApiFailedException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// API 参数读取器。参数可能来自 query、urlencoded 表单或 JSON，
/// 前两者均为字符串，这里统一做宽松的类型转换。
/// </summary>
public sealed class ApiParams
{
    private readonly JsonObject _obj;

    public ApiParams(JsonObject? obj)
    {
        _obj = obj ?? new JsonObject();
    }

    public bool Has(string name) => _obj.ContainsKey(name) && _obj[name] != null;

    public JsonNode? Node(string name) => _obj[name];

    public string Str(string name, string def = "", bool required = false)
    {
        if (!Has(name))
        {
            if (required)
            {
                throw new ApiParamException($"缺少参数 {name}");
            }

            return def;
        }

        return Core.JsonValues.ToText(_obj[name]);
    }

    public long Long(string name, long def = 0, bool required = false)
    {
        if (!Has(name))
        {
            if (required)
            {
                throw new ApiParamException($"缺少参数 {name}");
            }

            return def;
        }

        if (Core.JsonValues.TryGetLong(_obj[name], out var value))
        {
            return value;
        }

        throw new ApiParamException($"参数 {name} 不是数字");
    }

    public int Int(string name, int def = 0, bool required = false) => (int)Long(name, def, required);

    public bool Bool(string name, bool def = false)
    {
        if (!Has(name))
        {
            return def;
        }

        if (Core.JsonValues.TryGetBool(_obj[name], out var value))
        {
            return value;
        }

        throw new ApiParamException($"参数 {name} 不是布尔值");
    }
}
