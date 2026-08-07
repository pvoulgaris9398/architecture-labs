using System.Net.WebSockets;
using System.Text;
using Server.Models;
using Server.Services;

namespace Server.WebSockets;

public sealed class WebSocketEndpoint
{
    private const int MaximumSendDelayMilliseconds = 2_000;
    private const int MaximumMessageBytes = 64 * 1024;
    private const int ReceiveBufferBytes = 4 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

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

        var sendDelayMilliseconds = 0;
        if (
            int.TryParse(context.Request.Query["sendDelayMs"], out var requestedDelay)
            && requestedDelay > 0
        )
        {
            sendDelayMilliseconds = Math.Min(requestedDelay, MaximumSendDelayMilliseconds);
        }

        var connection = new ClientConnection
        {
            Socket = socket,
            OutboundSendDelay = TimeSpan.FromMilliseconds(sendDelayMilliseconds),
        };

        _connections.Add(connection);

        Console.WriteLine(
            $"Client connected: {connection.Id} (mode={connection.Mode}, sendDelayMs={sendDelayMilliseconds})"
        );

        // Start the outbound sender loop.
        var senderTask = _sender.RunAsync(connection);

        try
        {
            while (
                socket.State == WebSocketState.Open && !linkedCancellation.IsCancellationRequested
            )
            {
                var message = await ReceiveMessageAsync(socket, linkedCancellation.Token);

                if (message.Kind == ReceivedMessageKind.Close)
                {
                    Console.WriteLine($"Client requested close: {connection.Id}");
                    break;
                }

                if (message.Kind == ReceivedMessageKind.Rejected)
                    break;

                connection.LastSeenUtc = DateTime.UtcNow;

                await _dispatcher.DispatchAsync(
                    connection,
                    message.Text!,
                    linkedCancellation.Token
                );
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

    private static async Task<ReceivedMessage> ReceiveMessageAsync(
        WebSocket socket,
        CancellationToken cancellationToken
    )
    {
        using var messageBytes = new MemoryStream();
        var buffer = new byte[ReceiveBufferBytes];

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
                return new ReceivedMessage(ReceivedMessageKind.Close);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                await RejectMessageAsync(
                    socket,
                    WebSocketCloseStatus.InvalidMessageType,
                    "Text messages only",
                    cancellationToken
                );
                return new ReceivedMessage(ReceivedMessageKind.Rejected);
            }

            if (messageBytes.Length + result.Count > MaximumMessageBytes)
            {
                await RejectMessageAsync(
                    socket,
                    WebSocketCloseStatus.MessageTooBig,
                    $"Message exceeds {MaximumMessageBytes} bytes",
                    cancellationToken
                );
                return new ReceivedMessage(ReceivedMessageKind.Rejected);
            }

            messageBytes.Write(buffer, 0, result.Count);

            if (!result.EndOfMessage)
                continue;

            try
            {
                return new ReceivedMessage(
                    ReceivedMessageKind.Text,
                    StrictUtf8.GetString(messageBytes.GetBuffer(), 0, (int)messageBytes.Length)
                );
            }
            catch (DecoderFallbackException)
            {
                await RejectMessageAsync(
                    socket,
                    WebSocketCloseStatus.InvalidPayloadData,
                    "Text message is not valid UTF-8",
                    cancellationToken
                );
                return new ReceivedMessage(ReceivedMessageKind.Rejected);
            }
        }
    }

    private static Task RejectMessageAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string reason,
        CancellationToken cancellationToken
    ) => socket.CloseOutputAsync(status, reason, cancellationToken);

    private enum ReceivedMessageKind
    {
        Text,
        Close,
        Rejected,
    }

    private sealed record ReceivedMessage(ReceivedMessageKind Kind, string? Text = null);
}
