using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Server.IntegrationTests;

public sealed class SlowClientBackpressureTests
{
    private const int BurstCount = 750;

    [Fact(Timeout = 30_000)]
    public async Task Slow_client_fills_queue_waits_and_drains_in_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var webSocketClient = factory.Server.CreateWebSocketClient();
        using var socket = await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/ws?sendDelayMs=5"),
            cancellationToken
        );

        var burstTask = client.PostAsJsonAsync(
            "/api/events/burst",
            new { count = BurstCount, messagePrefix = "integration-test" },
            cancellationToken
        );

        var maximumDepth = await ObserveMaximumQueueDepthAsync(
            client,
            burstTask,
            cancellationToken
        );

        using var response = await burstTask;
        response.EnsureSuccessStatusCode();

        var sequences = await ReceiveSequencesAsync(socket, BurstCount, cancellationToken);
        await WaitForQueueToDrainAsync(client, cancellationToken);

        var metrics = await client.GetStringAsync("/metrics", cancellationToken);
        var waits = ReadMetric(
            metrics,
            "websocket_outbound_backpressure_waits_total",
            "connection_mode=\"slow\""
        );

        Assert.Equal(500, maximumDepth);
        Assert.True(waits > 0, $"Expected backpressure waits, but observed {waits}.");
        Assert.Equal(Enumerable.Range(1, BurstCount).Select(value => (long)value), sequences);
        Assert.Equal(
            0,
            ReadMetric(metrics, "websocket_outbound_queue_depth", "connection_mode=\"slow\"")
        );

        await socket.CloseAsync(
            WebSocketCloseStatus.NormalClosure,
            "Integration test complete",
            cancellationToken
        );
    }

    private static async Task<int> ObserveMaximumQueueDepthAsync(
        HttpClient client,
        Task<HttpResponseMessage> burstTask,
        CancellationToken cancellationToken
    )
    {
        var maximumDepth = 0;

        while (!burstTask.IsCompleted)
        {
            var metrics = await client.GetStringAsync("/metrics", cancellationToken);
            maximumDepth = Math.Max(
                maximumDepth,
                (int)ReadMetric(
                    metrics,
                    "websocket_outbound_queue_depth",
                    "connection_mode=\"slow\""
                )
            );
            await Task.Delay(10, cancellationToken);
        }

        return maximumDepth;
    }

    private static async Task<IReadOnlyList<long>> ReceiveSequencesAsync(
        WebSocket socket,
        int count,
        CancellationToken cancellationToken
    )
    {
        var sequences = new List<long>(count);
        var buffer = new byte[4096];

        while (sequences.Count < count)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            Assert.True(result.EndOfMessage);

            using var document = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
            sequences.Add(document.RootElement.GetProperty("Sequence").GetInt64());
        }

        return sequences;
    }

    private static async Task WaitForQueueToDrainAsync(
        HttpClient client,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var metrics = await client.GetStringAsync("/metrics", cancellationToken);
            if (
                ReadMetric(metrics, "websocket_outbound_queue_depth", "connection_mode=\"slow\"")
                == 0
            )
            {
                return;
            }

            await Task.Delay(25, cancellationToken);
        }

        Assert.Fail("The slow client's outbound queue did not drain within five seconds.");
    }

    private static double ReadMetric(string metrics, string name, string requiredLabel)
    {
        var line = metrics
            .Split('\n')
            .FirstOrDefault(candidate =>
                candidate.StartsWith(name, StringComparison.Ordinal)
                && candidate.Contains(requiredLabel, StringComparison.Ordinal)
            );

        Assert.NotNull(line);
        return double.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]);
    }
}
