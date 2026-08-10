using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LoadGenerator;

internal sealed class WebSocketAdapter(Uri baseUri) : ITransportAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public TransportDiagnostics Diagnostics { get; } = new();

    public async Task<ISubscriber> StartSubscriberAsync(
        int subscriber,
        Action<Delivery> onDelivery,
        CancellationToken cancellationToken
    )
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var socket = new ClientWebSocket();
        var builder = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Path = "/ws",
            Query = "",
        };
        Diagnostics.WebSocketUpgradeAttempted();
        await socket.ConnectAsync(builder.Uri, linked.Token);
        var completion = ReceiveAsync(socket, subscriber, onDelivery, linked.Token);
        return new Subscriber(Task.CompletedTask, completion, linked, socket);
    }

    private static async Task ReceiveAsync(
        ClientWebSocket socket,
        int subscriber,
        Action<Delivery> onDelivery,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[128 * 1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            var length = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                length += result.Count;
                if (length == buffer.Length && !result.EndOfMessage)
                    throw new InvalidOperationException("WebSocket event exceeded 128 KiB.");
            } while (!result.EndOfMessage);

            var record = JsonSerializer.Deserialize<EventRecord>(buffer.AsSpan(0, length), JsonOptions);
            if (record is not null)
                onDelivery(new Delivery(subscriber, record, Stopwatch.GetTimestamp()));
        }
    }
}
