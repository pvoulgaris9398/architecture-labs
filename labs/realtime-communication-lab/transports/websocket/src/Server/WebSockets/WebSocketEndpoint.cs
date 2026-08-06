using System.Net.WebSockets;
using System.Text;
using Server.Models;
using Server.Services;

namespace Server.WebSockets;

public sealed class WebSocketEndpoint
{
    private readonly ConnectionManager _connections;
    private readonly SocketDispatcher _dispatcher;
    private readonly ConnectionSender _sender;

    public WebSocketEndpoint(
        ConnectionManager connections,
        SocketDispatcher dispatcher,
        ConnectionSender sender
    )
    {
        _connections = connections;
        _dispatcher = dispatcher;
        _sender = sender;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket request expected.");
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted
        );

        var socket = await context.WebSockets.AcceptWebSocketAsync();

        var connection = new ClientConnection { Socket = socket };

        _connections.Add(connection);

        Console.WriteLine($"Client connected: {connection.Id}");

        // Start the outbound sender loop.
        var senderTask = _sender.RunAsync(connection);

        var buffer = new byte[8192];

        try
        {
            while (
                socket.State == WebSocketState.Open && !linkedCancellation.IsCancellationRequested
            )
            {
                var result = await socket.ReceiveAsync(buffer, linkedCancellation.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Console.WriteLine($"Client requested close: {connection.Id}");
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    Console.WriteLine($"Ignoring non-text message from {connection.Id}");
                    continue;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                connection.LastSeenUtc = DateTime.UtcNow;

                await _dispatcher.DispatchAsync(connection, json, linkedCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"Connection cancelled: {connection.Id}");
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine($"WebSocket error ({connection.Id}): {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unhandled exception ({connection.Id}): {ex}");
        }
        finally
        {
            Console.WriteLine($"Cleaning up connection {connection.Id}");

            _connections.Remove(connection.Id);

            // Stop the sender loop.
            connection.Cancellation.Cancel();

            connection.Outbound.Writer.TryComplete();

            try
            {
                await senderTask;
            }
            catch
            {
                // Ignore exceptions from shutdown.
            }

            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed",
                        CancellationToken.None
                    );
                }
                catch
                {
                    // Socket may already be gone.
                }
            }

            Console.WriteLine($"Client disconnected: {connection.Id}");
        }
    }
}
