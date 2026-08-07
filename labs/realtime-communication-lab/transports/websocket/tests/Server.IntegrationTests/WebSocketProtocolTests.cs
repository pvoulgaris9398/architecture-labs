using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Server.IntegrationTests;

public sealed class WebSocketProtocolTests
{
    [Fact(Timeout = 10_000)]
    public async Task Fragmented_text_message_is_reassembled_before_dispatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await ConnectAsync(factory, cancellationToken);

        await socket.SendAsync(
            Encoding.UTF8.GetBytes("{\"type\":"),
            WebSocketMessageType.Text,
            endOfMessage: false,
            cancellationToken
        );
        await socket.SendAsync(
            Encoding.UTF8.GetBytes("\"ping\"}"),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken
        );

        var json = await ReceiveTextAsync(socket, cancellationToken);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("pong", document.RootElement.GetProperty("Type").GetString());
        await CloseNormallyAsync(socket, cancellationToken);
    }

    [Fact(Timeout = 10_000)]
    public async Task Oversized_text_message_is_closed_with_message_too_big()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await ConnectAsync(factory, cancellationToken);
        var oversizedPayload = Encoding.UTF8.GetBytes(new string('x', 64 * 1024 + 1));

        await socket.SendAsync(
            oversizedPayload,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken
        );

        await AssertCloseStatusAsync(socket, WebSocketCloseStatus.MessageTooBig, cancellationToken);
    }

    [Fact(Timeout = 10_000)]
    public async Task Binary_message_is_closed_with_invalid_message_type()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await ConnectAsync(factory, cancellationToken);

        await socket.SendAsync(
            new byte[] { 1, 2, 3 },
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken
        );

        await AssertCloseStatusAsync(
            socket,
            WebSocketCloseStatus.InvalidMessageType,
            cancellationToken
        );
    }

    [Fact(Timeout = 10_000)]
    public async Task Invalid_utf8_is_closed_with_invalid_payload_data()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var socket = await ConnectAsync(factory, cancellationToken);

        await socket.SendAsync(
            new byte[] { 0xC3, 0x28 },
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken
        );

        await AssertCloseStatusAsync(
            socket,
            WebSocketCloseStatus.InvalidPayloadData,
            cancellationToken
        );
    }

    private static Task<WebSocket> ConnectAsync(
        WebApplicationFactory<Program> factory,
        CancellationToken cancellationToken
    ) =>
        factory
            .Server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/ws"), cancellationToken);

    private static async Task<string> ReceiveTextAsync(
        WebSocket socket,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[4096];
        var result = await socket.ReceiveAsync(buffer, cancellationToken);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        Assert.True(result.EndOfMessage);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static async Task AssertCloseStatusAsync(
        WebSocket socket,
        WebSocketCloseStatus expectedStatus,
        CancellationToken cancellationToken
    )
    {
        var result = await socket.ReceiveAsync(new byte[256], cancellationToken);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(expectedStatus, result.CloseStatus);
    }

    private static Task CloseNormallyAsync(WebSocket socket, CancellationToken cancellationToken) =>
        socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Integration test complete",
            cancellationToken
        );
}
