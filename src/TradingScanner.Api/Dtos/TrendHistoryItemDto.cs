namespace TradingScanner.Api.Dtos;

public sealed record TrendHistoryItemDto
{
    public string Symbol { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public string PreviousDirection { get; init; } = string.Empty;
    public string PreviousCondition { get; init; } = string.Empty;
    public string NewDirection { get; init; } = string.Empty;
    public string NewCondition { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
    public string? Reason { get; init; }
    public decimal? UpperBoundaryPrice { get; init; }
    public decimal? LowerBoundaryPrice { get; init; }
    public decimal? Atr { get; init; }
}