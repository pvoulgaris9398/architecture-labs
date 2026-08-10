using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace LoadGenerator;

internal sealed class SseAdapter(Uri baseUri, HttpMessageHandler handler) : ITransportAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public TransportDiagnostics Diagnostics { get; } = new();

    public async Task<ISubscriber> StartSubscriberAsync(
        int subscriber,
        Action<Delivery> onDelivery,
        CancellationToken cancellationToken
    )
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = baseUri };
        var request = new HttpRequestMessage(HttpMethod.Get, "/events/stream");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        Diagnostics.SseStreamRequested();
        var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            linked.Token
        );
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(linked.Token);
        var reader = new StreamReader(stream);
        var completion = ReceiveAsync(reader, response, subscriber, onDelivery, linked.Token);
        return new Subscriber(Task.CompletedTask, completion, linked);
    }

    private static async Task ReceiveAsync(
        StreamReader reader,
        HttpResponseMessage response,
        int subscriber,
        Action<Delivery> onDelivery,
        CancellationToken cancellationToken
    )
    {
        using (response)
        using (reader)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    return;
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                    continue;
                var record = JsonSerializer.Deserialize<EventRecord>(line.AsSpan(6), JsonOptions);
                if (record is not null)
                    onDelivery(new Delivery(subscriber, record, Stopwatch.GetTimestamp()));
            }
        }
    }
}
