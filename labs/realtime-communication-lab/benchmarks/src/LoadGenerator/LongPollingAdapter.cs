using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace LoadGenerator;

internal sealed class LongPollingAdapter(
    Uri baseUri,
    HttpMessageHandler handler,
    TimeSpan pollTimeout
) : ITransportAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public TransportDiagnostics Diagnostics { get; } = new();

    public Task<ISubscriber> StartSubscriberAsync(
        int subscriber,
        Action<Delivery> onDelivery,
        CancellationToken cancellationToken
    )
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = baseUri };
        var completion = PollAsync(client, subscriber, onDelivery, ready, linked.Token);
        return Task.FromResult<ISubscriber>(new Subscriber(ready.Task, completion, linked));
    }

    private async Task PollAsync(
        HttpClient client,
        int subscriber,
        Action<Delivery> onDelivery,
        TaskCompletionSource ready,
        CancellationToken cancellationToken
    )
    {
        long cursor = 0;
        var firstRequest = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            var path = $"/api/events/poll?since={cursor}&timeoutSeconds={(int)pollTimeout.TotalSeconds}";
            Diagnostics.LongPollRequested();
            var responseTask = client.GetAsync(path, cancellationToken);
            if (firstRequest)
            {
                ready.TrySetResult();
                firstRequest = false;
            }

            using var response = await responseTask;
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                Diagnostics.LongPollTimedOut();
                continue;
            }
            response.EnsureSuccessStatusCode();
            var records = await response.Content.ReadFromJsonAsync<EventRecord[]>(
                JsonOptions,
                cancellationToken
            ) ?? [];
            var receivedAt = Stopwatch.GetTimestamp();
            foreach (var record in records)
            {
                cursor = Math.Max(cursor, record.Sequence);
                onDelivery(new Delivery(subscriber, record, receivedAt));
            }
        }
    }
}
