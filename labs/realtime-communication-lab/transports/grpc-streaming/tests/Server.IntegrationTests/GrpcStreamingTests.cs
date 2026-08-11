using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Realtime.Grpc;
using Xunit;

namespace Server.IntegrationTests;

public sealed class GrpcStreamingTests
{
    [Fact(Timeout = 10_000)]
    public async Task Subscriber_receives_live_events_in_publish_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var channel = CreateChannel(factory);
        var client = new RealtimeTransport.RealtimeTransportClient(channel);
        using var stream = client.Subscribe(
            new SubscribeRequest(),
            cancellationToken: cancellationToken
        );
        await client.PublishAsync(
            new PublishRequest { Message = "first" },
            cancellationToken: cancellationToken
        );
        await client.PublishAsync(
            new PublishRequest { Message = "second" },
            cancellationToken: cancellationToken
        );

        Assert.True(await stream.ResponseStream.MoveNext(cancellationToken));
        var first = stream.ResponseStream.Current;
        Assert.True(await stream.ResponseStream.MoveNext(cancellationToken));
        var second = stream.ResponseStream.Current;

        Assert.Equal(new long[] { 1, 2 }, new[] { first.Id, second.Id });
        Assert.Equal(new[] { "first", "second" }, new[] { first.Message, second.Message });
    }

    [Fact(Timeout = 10_000)]
    public async Task After_id_replays_only_missed_events()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var channel = CreateChannel(factory);
        var client = new RealtimeTransport.RealtimeTransportClient(channel);
        await client.PublishAsync(new PublishRequest { Message = "first" }, cancellationToken: cancellationToken);
        await client.PublishAsync(new PublishRequest { Message = "second" }, cancellationToken: cancellationToken);
        await client.PublishAsync(new PublishRequest { Message = "third" }, cancellationToken: cancellationToken);
        using var stream = client.Subscribe(
            new SubscribeRequest { AfterId = 1 },
            cancellationToken: cancellationToken
        );

        Assert.True(await stream.ResponseStream.MoveNext(cancellationToken));
        var second = stream.ResponseStream.Current;
        Assert.True(await stream.ResponseStream.MoveNext(cancellationToken));
        var third = stream.ResponseStream.Current;

        Assert.Equal(new long[] { 2, 3 }, new[] { second.Id, third.Id });
    }

    [Fact(Timeout = 10_000)]
    public async Task Empty_publish_is_rejected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var channel = CreateChannel(factory);
        var client = new RealtimeTransport.RealtimeTransportClient(channel);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.PublishAsync(
                new PublishRequest { Message = "  " },
                cancellationToken: cancellationToken
            )
        );

        Assert.Equal(StatusCode.InvalidArgument, exception.StatusCode);
    }

    private static GrpcChannel CreateChannel(WebApplicationFactory<Program> factory) =>
        GrpcChannel.ForAddress(
            "http://localhost",
            new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() }
        );
}
