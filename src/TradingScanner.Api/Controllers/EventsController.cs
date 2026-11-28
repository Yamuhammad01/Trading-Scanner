using Microsoft.AspNetCore.Mvc;
using TradingScanner.Api.Dtos;
using TradingScanner.Api.Services;

namespace TradingScanner.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
[Produces("application/json")]
public sealed class EventsController : ControllerBase
{
    private readonly ScannerEventBroadcaster _broadcaster;

    public EventsController(ScannerEventBroadcaster broadcaster)
    {
        _broadcaster = broadcaster;
    }

    [HttpGet]
    [ProducesResponseType(typeof(object), 200)]
    public IActionResult GetEvents(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? eventType = null,
        [FromQuery] string? symbol = null,
        [FromQuery] string? severity = null,
        [FromQuery] DateTimeOffset? startDate = null,
        [FromQuery] DateTimeOffset? endDate = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 50;

        // Events are streamed via SignalR and stored in-memory by the broadcaster.
        // Return an empty paged response; the frontend should use SignalR for real-time events.
        return Ok(new
        {
            Items = Array.Empty<ScannerEventDto>(),
            Page = page,
            PageSize = pageSize,
            Total = 0,
            TotalPages = 0,
            Note = "Historical event persistence is not yet implemented. Use SignalR /hubs/scanner for real-time events."
        });
    }
}