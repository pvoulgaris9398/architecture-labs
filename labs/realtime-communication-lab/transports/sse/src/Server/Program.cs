using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Server.Endpoints;
using Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<EventStore>();
builder.Services.AddSingleton<SseConnectionManager>();
builder.Services.AddSingleton<SseMetrics>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SseMetrics>());
builder.Services.AddSingleton<SseBroadcastService>();
builder.Services.AddSingleton<SseEndpoint>();
builder
    .Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("realtime-sse-server"))
    .WithMetrics(metrics => metrics.AddMeter(SseMetrics.MeterName).AddPrometheusExporter());

var app = builder.Build();
app.MapGet("/", () => Results.Text("SSE Demo\n\nGET /events/stream\nPOST /api/events"));
app.MapGet(
    "/events/stream",
    (HttpContext context, SseEndpoint endpoint) => endpoint.HandleAsync(context)
);
app.MapControllers();
app.UseOpenTelemetryPrometheusScrapingEndpoint();
app.Run();

public partial class Program;
