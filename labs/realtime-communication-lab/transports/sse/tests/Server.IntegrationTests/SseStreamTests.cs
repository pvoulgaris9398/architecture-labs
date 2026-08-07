using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Server.IntegrationTests;

public sealed class SseStreamTests
{
    [Fact(Timeout = 10_000)]
    public async Task Published_event_arrives_with_id_event_and_data_fields()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var streamResponse = await OpenStreamAsync(
            client,
            "/events/stream",
            cancellationToken
        );
        await using var stream = await streamResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        await client.PostAsJsonAsync("/api/events", new { message = "first" }, cancellationToken);

        var received = await ReadEventAsync(reader, cancellationToken);

        Assert.Equal(1, received.Id);
        Assert.Equal("message", received.EventType);
        Assert.Equal("first", received.Message);
    }

    [Fact(Timeout = 10_000)]
    public async Task Last_event_id_replays_only_missed_events_in_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/events", new { message = "first" }, cancellationToken);
        await client.PostAsJsonAsync("/api/events", new { message = "second" }, cancellationToken);
        await client.PostAsJsonAsync("/api/events", new { message = "third" }, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/events/stream");
        request.Headers.Add("Last-Event-ID", "1");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var second = await ReadEventAsync(reader, cancellationToken);
        var third = await ReadEventAsync(reader, cancellationToken);

        Assert.Equal(new long[] { 2, 3 }, new[] { second.Id, third.Id });
        Assert.Equal(new[] { "second", "third" }, new[] { second.Message, third.Message });
    }

    private static async Task<HttpResponseMessage> OpenStreamAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken
    )
    {
        var response = await client.GetAsync(
            path,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return response;
    }

    private static async Task<ReceivedEvent> ReadEventAsync(
        StreamReader reader,
        CancellationToken cancellationToken
    )
    {
        var idLine = await reader.ReadLineAsync(cancellationToken);
        var eventLine = await reader.ReadLineAsync(cancellationToken);
        var dataLine = await reader.ReadLineAsync(cancellationToken);
        var separator = await reader.ReadLineAsync(cancellationToken);
        Assert.NotNull(idLine);
        Assert.NotNull(eventLine);
        Assert.NotNull(dataLine);
        Assert.Equal(string.Empty, separator);
        using var data = JsonDocument.Parse(dataLine[6..]);
        return new ReceivedEvent(
            long.Parse(idLine[4..]),
            eventLine[7..],
            data.RootElement.GetProperty("message").GetString()!
        );
    }

    private sealed record ReceivedEvent(long Id, string EventType, string Message);
}
