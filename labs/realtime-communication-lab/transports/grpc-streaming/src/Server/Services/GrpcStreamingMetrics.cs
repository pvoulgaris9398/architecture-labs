using System.Diagnostics.Metrics;

namespace Server.Services;

public sealed class GrpcStreamingMetrics
{
    public const string MeterName = "Realtime.GrpcStreaming";
    private readonly Counter<long> published;
    private readonly Counter<long> delivered;
    private readonly Histogram<double> deliveryDelay;

    public GrpcStreamingMetrics(GrpcEventHub hub)
    {
        var meter = new Meter(MeterName);
        published = meter.CreateCounter<long>("grpc_streaming.events.published");
        delivered = meter.CreateCounter<long>("grpc_streaming.events.delivered");
        deliveryDelay = meter.CreateHistogram<double>("grpc_streaming.delivery.delay", "ms");
        meter.CreateObservableGauge(
            "grpc_streaming.subscribers.active",
            () => hub.SubscriberCount
        );
    }

    public void EventPublished() => published.Add(1);
    public void EventDelivered(double delayMilliseconds)
    {
        delivered.Add(1);
        deliveryDelay.Record(delayMilliseconds);
    }
}
