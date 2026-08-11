using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<GrpcEventHub>();
builder.Services.AddSingleton<GrpcStreamingMetrics>();
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("realtime-grpc-streaming-server"))
    .WithMetrics(metrics =>
        metrics.AddMeter(GrpcStreamingMetrics.MeterName).AddPrometheusExporter()
    );

var app = builder.Build();
app.MapGrpcService<RealtimeTransportService>();
app.MapGet(
    "/",
    () => Results.Text("gRPC streaming demo\n\nUse a gRPC client with Protos/realtime.proto")
);
app.UseOpenTelemetryPrometheusScrapingEndpoint();
app.Run();

public partial class Program;
