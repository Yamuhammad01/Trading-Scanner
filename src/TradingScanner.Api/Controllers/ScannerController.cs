using Microsoft.AspNetCore.Mvc;
using TradingScanner.Api.Dtos;
using TradingScanner.Api.Services;

namespace TradingScanner.Api.Controllers;

[ApiController]
[Route("api/v1/scanner")]
[Produces("application/json")]
public sealed class ScannerController : ControllerBase
{
    private readonly MarketDataQueryService _queryService;

    public ScannerController(MarketDataQueryService queryService)
    {
        _queryService = queryService;
    }

    [HttpGet("status")]
    [ProducesResponseType(typeof(ScannerStatusDto), 200)]
    public IActionResult GetStatus()
    {
        var status = _queryService.GetScannerStatus();
        return Ok(status);
    }
}