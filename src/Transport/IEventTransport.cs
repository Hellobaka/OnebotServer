using System.Text.Json.Nodes;

namespace OnebotServer.Transport;

/// <summary>能够推送事件的传输层（正向 WebSocket / 反向 WebSocket）。</summary>
public interface IEventTransport
{
    string Name { get; }

    Task PushEventAsync(JsonObject evt);

    void Stop();
}
