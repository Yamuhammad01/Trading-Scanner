using Microsoft.AspNetCore.Mvc;
using TradingScanner.Api.Dtos;
using TradingScanner.Api.Services;

namespace TradingScanner.Api.Controllers;

[ApiController]
[Route("api/v1/markets")]
[Produces("application/json")]
public sealed class MarketsController : ControllerBase
{
    private readonly MarketDataQueryService _queryService;

    public MarketsController(MarketDataQueryService queryService)
    {
        _queryService = queryService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(object), 200)]
    public IActionResult GetMarkets(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        [FromQuery] string? direction = null,
        [FromQuery] string? condition = null,
        [FromQuery] string? status = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 50;

        var items = _queryService.GetMarkets(page, pageSize, search, direction, condition, status, sortBy, sortDirection);
        var total = _queryService.GetMarketsCount(search, direction, condition, status);
        var totalPages = (int)Math.Ceiling((double)total / pageSize);

        return Ok(new
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = totalPages
        });
    }

    [HttpGet("{symbol}")]
    [ProducesResponseType(typeof(MarketDetailDto), 200)]
    [ProducesResponseType(404)]
    public IActionResult GetMarket(string symbol)
    {
        var market = _queryService.GetMarketDetail(symbol);
        if (market is null)
            return NotFound(new { error = $"Market '{symbol}' not found." });

        return Ok(market);
    }

    [HttpGet("{symbol}/candles")]
    [ProducesResponseType(typeof(IEnumerable<TradingScanner.Api.Dtos.CandleDto>), 200)]
    public async Task<IActionResult> GetCandles(
        string symbol,
        [FromQuery] string timeframe = "4H",
        [FromQuery] DateTimeOffset? startTime = null,
        [FromQuery] DateTimeOffset? endTime = null,
        [FromQuery] int? limit = null)
    {
        if (limit < 1 || limit > 500) limit = 100;

        var candles = await _queryService.GetCandlesAsync(symbol, timeframe, startTime, endTime, limit);
        return Ok(candles);
    }

    [HttpGet("{symbol}/trends")]
    [ProducesResponseType(typeof(IEnumerable<TradingScanner.Api.Dtos.TrendHistoryItemDto>), 200)]
    public async Task<IActionResult> GetTrendHistory(string symbol)
    {
        var history = await _queryService.GetTrendHistoryAsync(symbol);
        return Ok(history);
    }
}