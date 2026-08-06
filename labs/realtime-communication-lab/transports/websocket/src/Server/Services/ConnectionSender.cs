using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Server.Models;

namespace Server.Services;

public sealed class ConnectionSender
{
    public async Task RunAsync(ClientConnection connection)
    {
        Console.WriteLine($"Sender started for {connection.Id}");

        try
        {
            await foreach (
                var message in connection.Outbound.Reader.ReadAllAsync(
                    connection.Cancellation.Token
                )
            )
            {
                if (connection.Socket.State != WebSocketState.Open)
                    break;

                var json = JsonSerializer.Serialize(message);

                var bytes = Encoding.UTF8.GetBytes(json);

                await connection.Socket.SendAsync(
                    bytes,
                    WebSocketMessageType.Text,
                    true,
                    connection.Cancellation.Token
                );

                Console.WriteLine($"Sent {message.Type} to {connection.Id}");
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Sender cancelled for {connection.Id}");
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"WebSocket error ({connection.Id}): {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Sender failed ({connection.Id}): {ex}");
        }

        Console.WriteLine($"Sender stopped for {connection.Id}");
    }
}
