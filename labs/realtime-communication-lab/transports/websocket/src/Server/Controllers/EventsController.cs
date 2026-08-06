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
}
