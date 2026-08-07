using Microsoft.AspNetCore.Mvc;
using Server.Models;
using Server.Services;

namespace Server.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly EventStore _store;
    private readonly BroadcastService _broadcast;

    public EventsController(EventStore store, BroadcastService broadcast)
    {
        _store = store;
        _broadcast = broadcast;
    }

    [HttpPost]
    public async Task<ActionResult<EventRecord>> Publish(PublishRequest request)
    {
        var record = _store.Append(request.Message);

        await _broadcast.BroadcastAsync(record);

        return Ok(record);
    }

    [HttpGet]
    public ActionResult<IEnumerable<EventRecord>> GetSince([FromQuery] long since = 0)
    {
        return Ok(_store.GetSince(since));
    }

    [HttpPost("burst")]
    public async Task<ActionResult<BurstPublishResult>> PublishBurst(
        BurstPublishRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.Count is < 1 or > 1_000)
            return BadRequest("Count must be between 1 and 1000.");

        if (string.IsNullOrWhiteSpace(request.MessagePrefix))
            return BadRequest("MessagePrefix is required.");

        long firstSequence = 0;
        long lastSequence = 0;

        for (var index = 1; index <= request.Count; index++)
        {
            var record = _store.Append($"{request.MessagePrefix}-{index:D4}");
            firstSequence = firstSequence == 0 ? record.Sequence : firstSequence;
            lastSequence = record.Sequence;
            await _broadcast.BroadcastAsync(record, cancellationToken);
        }

        return Ok(new BurstPublishResult(request.Count, firstSequence, lastSequence));
    }
}
