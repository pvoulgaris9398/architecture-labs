using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController, Route("api/events")]
public sealed class EventsController : ControllerBase
{
    private readonly EventStore _store;
    private readonly LongPollingMetrics _metrics;

    public EventsController(EventStore store, LongPollingMetrics metrics)
    {
        _store = store;
        _metrics = metrics;
    }

    [HttpPost]
    public ActionResult<EventRecord> Publish(PublishRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message is required.");
        return Ok(_store.Append(request.Message));
    }

    [HttpGet("poll")]
    public async Task<ActionResult<IReadOnlyList<EventRecord>>> Poll(
        [FromQuery] long since = 0,
        [FromQuery] int timeoutSeconds = 30,
        CancellationToken cancellationToken = default
    )
    {
        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 60);
        var started = _metrics.Start();
        try
        {
            var events = await _store.PollAsync(
                since,
                TimeSpan.FromSeconds(timeoutSeconds),
                cancellationToken
            );
            _metrics.Complete(started, events.Count == 0 ? "timeout" : "events", events.Count);
            return events.Count == 0 ? NoContent() : Ok(events);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _metrics.Complete(started, "cancelled", 0);
            throw;
        }
    }

    [HttpPost("burst")]
    public ActionResult<BurstPublishResult> Burst(BurstPublishRequest request)
    {
        if (request.Count is < 1 or > 1000 || string.IsNullOrWhiteSpace(request.MessagePrefix))
            return BadRequest();
        long first = 0,
            last = 0;
        for (var index = 1; index <= request.Count; index++)
        {
            var record = _store.Append($"{request.MessagePrefix}-{index:D4}");
            first = first == 0 ? record.Sequence : first;
            last = record.Sequence;
        }
        return Ok(new BurstPublishResult(request.Count, first, last));
    }
}
