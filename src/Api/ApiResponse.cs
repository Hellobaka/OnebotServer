using System.Text.Json.Nodes;

namespace OnebotServer.Api;

/// <summary>OneBot 响应返回码（自定义部分沿用常见 OneBot 实现的取值）。</summary>
public static class RetCode
{
    /// <summary>操作成功。</summary>
    public const int Ok = 0;

    /// <summary>请求已提交异步处理。</summary>
    public const int Async = 1;

    /// <summary>参数缺失或不合法。</summary>
    public const int ParamError = 100;

    /// <summary>操作执行失败。</summary>
    public const int Failed = 200;

    /// <summary>请求格式不正确（HTTP 400）。</summary>
    public const int BadRequest = 1400;

    /// <summary>API 不存在（HTTP 404）。</summary>
    public const int NotFound = 1404;
}

/// <summary>OneBot API 响应，结构为 {status, retcode, data, echo}。</summary>
public sealed class ApiResponse
{
    public string Status { get; private init; } = "ok";

    public int Retcode { get; private init; }

    public JsonNode? Data { get; private init; }

    public JsonNode? Echo { get; private init; }

    public bool HasEcho { get; private init; }

    public static ApiResponse Ok(JsonNode? data, JsonNode? echo, bool hasEcho) => new()
    {
        Status = "ok",
        Retcode = RetCode.Ok,
        Data = data,
        Echo = echo,
        HasEcho = hasEcho,
    };

    public static ApiResponse Async(JsonNode? echo, bool hasEcho) => new()
    {
        Status = "async",
        Retcode = RetCode.Async,
        Data = null,
        Echo = echo,
        HasEcho = hasEcho,
    };

    public static ApiResponse Fail(int retcode, JsonNode? echo, bool hasEcho) => new()
    {
        Status = "failed",
        Retcode = retcode,
        Data = null,
        Echo = echo,
        HasEcho = hasEcho,
    };

    /// <summary>HTTP 状态码：鉴权等传输层错误由各传输层自行返回，此处只处理调用结果。</summary>
    public int HttpStatus => Retcode switch
    {
        RetCode.BadRequest => 400,
        RetCode.NotFound => 404,
        _ => 200,
    };

    public JsonObject ToJson()
    {
        var obj = new JsonObject
        {
            ["status"] = Status,
            ["retcode"] = Retcode,
            ["data"] = Data?.DeepClone(),
        };
        if (HasEcho)
        {
            obj["echo"] = Echo?.DeepClone();
        }

        return obj;
    }
}
