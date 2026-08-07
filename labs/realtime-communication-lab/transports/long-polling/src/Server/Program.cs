using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Server.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddSingleton<EventStore>();
builder.Services.AddSingleton<LongPollingMetrics>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<LongPollingMetrics>());
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("realtime-long-polling-server"))
    .WithMetrics(metrics => metrics.AddMeter(LongPollingMetrics.MeterName).AddPrometheusExporter());
var app = builder.Build();
app.MapGet(
    "/",
    () =>
        Results.Text(
            "Long Polling Demo\n\nGET /api/events/poll?since=0&timeoutSeconds=30\nPOST /api/events"
        )
);
app.MapControllers();
app.UseOpenTelemetryPrometheusScrapingEndpoint();
app.Run();

public partial class Program;
