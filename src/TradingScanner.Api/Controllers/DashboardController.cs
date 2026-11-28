using Microsoft.AspNetCore.Mvc;
using TradingScanner.Api.Dtos;
using TradingScanner.Api.Services;

namespace TradingScanner.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Produces("application/json")]
public sealed class DashboardController : ControllerBase
{
    private readonly MarketDataQueryService _queryService;

    public DashboardController(MarketDataQueryService queryService)
    {
        _queryService = queryService;
    }

    [HttpGet("overview")]
    [ProducesResponseType(typeof(MarketOverviewDto), 200)]
    public IActionResult GetOverview()
    {
        var overview = _queryService.GetDashboardOverview();
        return Ok(overview);
    }
}