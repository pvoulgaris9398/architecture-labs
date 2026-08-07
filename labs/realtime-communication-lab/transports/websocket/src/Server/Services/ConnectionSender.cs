using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Server.Models;

namespace Server.Services;

public sealed class ConnectionSender
{
    private readonly WebSocketMetrics _metrics;

    public ConnectionSender(WebSocketMetrics metrics)
    {
        _metrics = metrics;
    }

    public async Task RunAsync(ClientConnection connection)
    {
        Console.WriteLine($"Sender started for {connection.Id}");

        try
        {
            await foreach (
                var queued in connection.Outbound.Reader.ReadAllAsync(connection.Cancellation.Token)
            )
            {
                if (connection.Socket.State != WebSocketState.Open)
                    break;

                if (connection.OutboundSendDelay > TimeSpan.Zero)
                {
                    await Task.Delay(connection.OutboundSendDelay, connection.Cancellation.Token);
                }

                var json = JsonSerializer.Serialize(queued.Message);

                var bytes = Encoding.UTF8.GetBytes(json);

                await connection.Socket.SendAsync(
                    bytes,
                    WebSocketMessageType.Text,
                    true,
                    connection.Cancellation.Token
                );

                _metrics.MessageSent(
                    connection.Mode,
                    queued.Message.Type,
                    queued.EnqueuedTimestamp
                );

                Console.WriteLine($"Sent {queued.Message.Type} to {connection.Id}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Sender cancelled for {connection.Id}");
        }
        catch (WebSocketException ex)
        {
            _metrics.SendFailed(nameof(WebSocketException));
            Console.WriteLine($"WebSocket error ({connection.Id}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _metrics.SendFailed(ex.GetType().Name);
            Console.WriteLine($"Sender failed ({connection.Id}): {ex}");
        }

        Console.WriteLine($"Sender stopped for {connection.Id}");
    }
}
