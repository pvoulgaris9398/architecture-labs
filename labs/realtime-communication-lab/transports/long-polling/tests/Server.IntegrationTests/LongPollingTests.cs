using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Server.IntegrationTests;

public sealed class LongPollingTests
{
    [Fact(Timeout = 10_000)]
    public async Task Pending_poll_completes_when_event_is_published()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var poll = client.GetAsync("/api/events/poll?since=0&timeoutSeconds=5", token);
        await Task.Delay(100, token);
        await client.PostAsJsonAsync("/api/events", new { message = "arrived" }, token);
        using var response = await poll;
        var events = await response.Content.ReadFromJsonAsync<EventRecord[]>(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(events!);
        Assert.Equal("arrived", events![0].Message);
    }

    [Fact(Timeout = 10_000)]
    public async Task Poll_returns_immediately_in_order_when_events_exist()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync(
            "/api/events/burst",
            new { count = 3, messagePrefix = "ordered" },
            token
        );
        var events = await client.GetFromJsonAsync<EventRecord[]>(
            "/api/events/poll?since=0&timeoutSeconds=5",
            token
        );
        Assert.Equal(new long[] { 1, 2, 3 }, events!.Select(e => e.Sequence));
    }

    [Fact(Timeout = 10_000)]
    public async Task Poll_returns_no_content_on_timeout()
    {
        var token = TestContext.Current.CancellationToken;
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(
            "/api/events/poll?since=0&timeoutSeconds=1",
            token
        );
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed record EventRecord(long Sequence, DateTime Timestamp, string Message);
}
