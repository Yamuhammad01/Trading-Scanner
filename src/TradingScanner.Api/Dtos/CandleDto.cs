namespace TradingScanner.Api.Dtos;

public sealed record CandleDto
{
    public string Symbol { get; init; } = string.Empty;
    public DateTimeOffset StartTime { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public decimal Turnover { get; init; }
}