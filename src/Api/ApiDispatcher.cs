using System.Text.Json.Nodes;
using System.Threading.Channels;
using OnebotServer.Core;

namespace OnebotServer.Api;

/// <summary>
/// OneBot API 调度：处理 _async / _rate_limited 后缀并路由到 ApiHandlers，
/// 统一产生 {status, retcode, data, echo} 结构的响应。
/// </summary>
public static class ApiDispatcher
{
    private static Channel<Func<Task>>? _rateQueue;
    private static readonly object RateLock = new();

    public static async Task<ApiResponse> DispatchAsync(string action, JsonObject? @params, JsonNode? echo, bool hasEcho)
    {
        action = action.Trim().Trim('/');

        // 所有 API 都可以附加 _async / _rate_limited 后缀，二者可叠加
        bool isAsync = false;
        bool isRateLimited = false;
        string core = action;
        bool changed = true;
        while (changed)
        {
            changed = false;
            if (core.EndsWith("_async", StringComparison.Ordinal))
            {
                isAsync = true;
                core = core[..^"_async".Length];
                changed = true;
            }
            else if (core.EndsWith("_rate_limited", StringComparison.Ordinal))
            {
                isRateLimited = true;
                core = core[..^"_rate_limited".Length];
                changed = true;
            }
        }

        if (isRateLimited)
        {
            EnqueueRateLimited(() => RunSilentAsync(core, @params));
            return ApiResponse.Async(echo, hasEcho);
        }

        if (isAsync)
        {
            _ = Task.Run(() => RunSilentAsync(core, @params));
            return ApiResponse.Async(echo, hasEcho);
        }

        try
        {
            var result = await ApiHandlers.ExecuteAsync(core, new ApiParams(@params));
            return result.ForceAsync
                ? ApiResponse.Async(echo, hasEcho)
                : ApiResponse.Ok(result.Data, echo, hasEcho);
        }
        catch (ApiNotFoundException ex)
        {
            PluginRuntime.Log("Warn", "API", ex.Message);
            return ApiResponse.Fail(RetCode.NotFound, echo, hasEcho);
        }
        catch (ApiParamException ex)
        {
            PluginRuntime.Log("Warn", "API", $"{core}: {ex.Message}");
            return ApiResponse.Fail(RetCode.ParamError, echo, hasEcho);
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Error", "API", $"{core} 执行失败: {ex.Message}");
            return ApiResponse.Fail(RetCode.Failed, echo, hasEcho);
        }
    }

    private static async Task RunSilentAsync(string action, JsonObject? @params)
    {
        try
        {
            await ApiHandlers.ExecuteAsync(action, new ApiParams(@params));
        }
        catch (ApiNotFoundException ex)
        {
            PluginRuntime.Log("Warn", "API", ex.Message);
        }
        catch (Exception ex)
        {
            PluginRuntime.Log("Error", "API", $"{action} 异步执行失败: {ex.Message}");
        }
    }

    /// <summary>限速调用按 api.rate_limit_interval 间隔排队执行。</summary>
    private static void EnqueueRateLimited(Func<Task> work)
    {
        lock (RateLock)
        {
            var queue = _rateQueue ??= StartRateWorker();
            queue.Writer.TryWrite(work);
        }
    }

    private static Channel<Func<Task>> StartRateWorker()
    {
        var queue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
        _ = Task.Run(async () =>
        {
            await foreach (var work in queue.Reader.ReadAllAsync())
            {
                try
                {
                    await work();
                }
                catch
                {
                    // 与 _async 一样不反馈结果
                }

                int interval = Math.Max(0, PluginRuntime.Config.Api.RateLimitInterval);
                if (interval > 0)
                {
                    await Task.Delay(interval);
                }
            }
        });
        return queue;
    }
}
