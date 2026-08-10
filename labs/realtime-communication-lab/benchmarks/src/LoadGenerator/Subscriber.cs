namespace LoadGenerator;

internal sealed class Subscriber : ISubscriber
{
    private readonly CancellationTokenSource _cancellation;
    private readonly IDisposable? _resource;

    public Subscriber(
        Task ready,
        Task completion,
        CancellationTokenSource cancellation,
        IDisposable? resource = null
    )
    {
        Ready = ready;
        Completion = completion;
        _cancellation = cancellation;
        _resource = resource;
    }

    public Task Ready { get; }
    public Task Completion { get; }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        try
        {
            await Completion;
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) when (_cancellation.IsCancellationRequested) { }

        if (_resource is not null)
            _resource.Dispose();
        _cancellation.Dispose();
    }
}
