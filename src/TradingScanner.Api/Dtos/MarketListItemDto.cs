namespace TradingScanner.Api.Dtos;

public sealed record MarketListItemDto
{
    public string Symbol { get; init; } = string.Empty;
    public string ExchangeSymbol { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public string InitializationStatus { get; init; } = string.Empty;
    public string MonitoringStatus { get; init; } = string.Empty;
    public string? TrendDirection { get; init; }
    public string? TrendCondition { get; init; }
    public DateTimeOffset? LastEvaluatedCandle { get; init; }
    public DateTimeOffset? LastMarketDataUpdate { get; init; }
    public decimal? CurrentPrice { get; init; }
    public int? Rank { get; init; }
    public decimal? QuoteVolume24h { get; init; }
}