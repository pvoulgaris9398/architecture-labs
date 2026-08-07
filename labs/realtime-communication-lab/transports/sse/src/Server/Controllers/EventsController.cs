using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/events")]
public sealed class EventsController : ControllerBase
{
    private readonly EventStore _store;
    private readonly SseBroadcastService _broadcast;

    public EventsController(EventStore store, SseBroadcastService broadcast)
    {
        _store = store;
        _broadcast = broadcast;
    }

    [HttpPost]
    public async Task<ActionResult<EventRecord>> Publish(
        PublishRequest request,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("Message is required.");
        var record = _store.Append(request.Message);
        await _broadcast.BroadcastAsync(record, cancellationToken);
        return Ok(record);
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<EventRecord>> GetSince([FromQuery] long since = 0) =>
        Ok(_store.GetSince(since));

    [HttpPost("burst")]
    public async Task<ActionResult<BurstPublishResult>> PublishBurst(
        BurstPublishRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.Count is < 1 or > 1_000 || string.IsNullOrWhiteSpace(request.MessagePrefix))
            return BadRequest("Count must be 1-1000 and MessagePrefix is required.");
        long first = 0;
        long last = 0;
        for (var index = 1; index <= request.Count; index++)
        {
            var record = _store.Append($"{request.MessagePrefix}-{index:D4}");
            first = first == 0 ? record.Sequence : first;
            last = record.Sequence;
            await _broadcast.BroadcastAsync(record, cancellationToken);
        }
        return Ok(new BurstPublishResult(request.Count, first, last));
    }
}
