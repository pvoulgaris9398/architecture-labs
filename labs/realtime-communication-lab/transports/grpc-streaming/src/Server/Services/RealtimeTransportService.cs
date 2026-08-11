using Grpc.Core;
using Realtime.Grpc;

namespace Server.Services;

public sealed class RealtimeTransportService(
    GrpcEventHub eventHub,
    GrpcStreamingMetrics metrics
) : RealtimeTransport.RealtimeTransportBase
{
    public override async Task<RealtimeEvent> Publish(
        PublishRequest request,
        ServerCallContext context
    )
    {
        var message = request.Message.Trim();
        if (string.IsNullOrEmpty(message))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "message is required"));
        }
        if (message.Length > 4096)
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "message must not exceed 4096 characters")
            );
        }

        var item = await eventHub.PublishAsync(message, context.CancellationToken);
        metrics.EventPublished();
        return item;
    }

    public override async Task Subscribe(
        SubscribeRequest request,
        IServerStreamWriter<RealtimeEvent> responseStream,
        ServerCallContext context
    )
    {
        if (request.AfterId < 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "after_id cannot be negative"));
        }
        if (request.SendDelayMs is < 0 or > 2000)
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "send_delay_ms must be between 0 and 2000")
            );
        }

        using var subscription = eventHub.Subscribe(request.AfterId);
        await foreach (
            var item in subscription.Reader.ReadAllAsync(context.CancellationToken)
        )
        {
            if (request.SendDelayMs > 0)
            {
                await Task.Delay(request.SendDelayMs, context.CancellationToken);
            }
            await responseStream.WriteAsync(item, context.CancellationToken);
            var createdAt = DateTimeOffset.Parse(item.CreatedAtUtc);
            metrics.EventDelivered((DateTimeOffset.UtcNow - createdAt).TotalMilliseconds);
        }
    }
}
