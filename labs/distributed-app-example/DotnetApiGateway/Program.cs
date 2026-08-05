using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions; // Standard namespaces
using Microsoft.Extensions.Caching.Distributed;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RabbitMQ.Client;

var postgresConn = Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSQL") 
    ?? "Host=localhost;Port=5432;Database=db_ecommerce;Username=dev_user;Password=dev_password";

var redisConn = Environment.GetEnvironmentVariable("ConnectionStrings__Redis") 
    ?? "localhost:6379";

var grpcUrl = Environment.GetEnvironmentVariable("ConnectionStrings__gRPCInventory") 
    ?? "http://localhost:50051";

var rabbitHost = Environment.GetEnvironmentVariable("ConnectionStrings__RabbitMQ") 
    ?? "localhost";

var builder = WebApplication.CreateBuilder(args);

// Register RabbitMQ Connection as a Singleton
builder.Services.AddSingleton<IConnectionFactory>(sp => new ConnectionFactory
{
    HostName = rabbitHost,
    Port = 5672,
    UserName = "guest",
    Password = "guest"
});

// ----------------------------------------------------
// 1. Storage & Caching Configurations
// ----------------------------------------------------
// var postgresConn = "Host=localhost;Port=5432;Database=ecom_db;Username=dev_user;Password=dev_password";
// var redisConn = "localhost:6379";

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(postgresConn));
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConn;
    options.InstanceName = "Catalog_";
});

// Configures the typed gRPC client to point to the Python backend port
builder.Services.AddGrpcClient<DotnetApiGateway.Protos.InventoryService.InventoryServiceClient>(o =>
{
    o.Address = new Uri(grpcUrl);
});


// ----------------------------------------------------
// 2. OpenTelemetry & Distributed Tracing Setup
// ----------------------------------------------------
// Define a custom activity source for application-level trace spans
var appSource = new ActivitySource("DotnetApiGateway.Core");
var appMeter = new Meter("DistributedApp.Gateway");
var productReadCounter = appMeter.CreateCounter<long>("distributed.gateway.product_reads");
var checkoutCounter = appMeter.CreateCounter<long>("distributed.gateway.checkouts");

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("DotnetApiGateway"))
        .AddSource("DotnetApiGateway.Core")
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddRedisInstrumentation() // Captures raw Redis commands automatically
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(
                Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
                ?? "http://localhost:4317");
        }))
    .WithMetrics(metrics => metrics
        .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("DotnetApiGateway"))
        .AddMeter("DistributedApp.Gateway")
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(
                Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
                ?? "http://localhost:4317");
        }));

// Allow any origin, method, and header for testing purposes
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
});

var app = builder.Build();

// Make sure to add the middleware right before map endpoints
app.UseCors();

// Auto-apply database schema migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// ----------------------------------------------------
// 3. API Endpoints (Cache-Aside Pattern)
// ----------------------------------------------------
app.MapGet("/products/{id:int}", async (
    int id, 
    IDistributedCache cache, 
    AppDbContext db, 
    DotnetApiGateway.Protos.InventoryService.InventoryServiceClient inventoryClient) =>
{
    productReadCounter.Add(1);
    using var activity = appSource.StartActivity("GetProductDetail");
    activity?.SetTag("product.id", id);

    string cacheKey = $"product:{id}";
    string? cachedJson = await cache.GetStringAsync(cacheKey);
    Product? product;

    if (!string.IsNullOrEmpty(cachedJson))
    {
        activity?.SetTag("cache.hit", true);
        product = JsonSerializer.Deserialize<Product>(cachedJson);
    }
    else
    {
        activity?.SetTag("cache.hit", false);
        product = await db.Products.FindAsync(id);
        if (product is not null)
        {
            await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(product), 
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
        }
    }

    if (product is null) return Results.NotFound();

    // Chaining the request: Call Python FastAPI via gRPC
    // Tracing context is automatically injected into the request metadata headers here
    var stockResponse = await inventoryClient.CheckStockAsync(new DotnetApiGateway.Protos.StockRequest { ProductId = id });

    return Results.Ok(new {
        DataSource = !string.IsNullOrEmpty(cachedJson) ? "Redis Cache" : "PostgreSQL Database",
        Data = product,
        StockInfo = new {
            Available = stockResponse.AvailableStock,
            InStock = stockResponse.IsInStock
        }
    });
});


// Helper endpoint to populate seed data directly into PostgreSQL
app.MapPost("/products/seed", async (AppDbContext db) =>
{
    if (!await db.Products.AnyAsync())
    {
        db.Products.AddRange(
            new Product { Name = "Gaming Laptop", Price = 1299.99m },
            new Product { Name = "Wireless Mouse", Price = 49.99m },
            new Product { Name = "Mechanical Keyboard", Price = 89.99m }
        );
        await db.SaveChangesAsync();
        return Results.Ok("Database seeded successfully.");
    }
    return Results.Ok("Database already contains data.");
});

app.MapPost("/checkout", async (CheckoutRequest request, IConnectionFactory factory) =>
{
    checkoutCounter.Add(1);
    // Custom tracing tracking span
    using var activity = appSource.StartActivity("PublishOrderEvent");
    activity?.SetTag("checkout.product_id", request.ProductId);

    // Establish connection to local RabbitMQ container
    using var connection = await factory.CreateConnectionAsync();
    using var channel = await connection.CreateChannelAsync();

    // Declare a durable topic exchange
    string exchangeName = "order_exchange";
    await channel.ExchangeDeclareAsync(exchange:exchangeName, type: ExchangeType.Topic, durable: true);

    // Create message payload
    var orderEvent = new { OrderId = Guid.NewGuid(), request.ProductId, Quantity = request.Quantity, Timestamp = DateTime.UtcNow };
    var messageBody = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(orderEvent);

    // Set up basic delivery properties
    var props = new BasicProperties { DeliveryMode = DeliveryModes.Persistent };

    // Publish message asynchronously to the exchange
    await channel.BasicPublishAsync(
        exchange: exchangeName,
        routingKey: "order.placed",
        mandatory: true,
        basicProperties: props,
        body: messageBody
    );

    activity?.SetTag("order.id", orderEvent.OrderId);
    return Results.Accepted(value: new { Status = "Order Submitted Asynchronously", OrderId = orderEvent.OrderId });
});

app.Run();

// ----------------------------------------------------
// 4. Data Models & Database Context
// ----------------------------------------------------
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Product> Products => Set<Product>();
}

// Data Model Record
public record CheckoutRequest(int ProductId, int Quantity);
