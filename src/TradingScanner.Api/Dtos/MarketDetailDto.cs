namespace TradingScanner.Api.Dtos;

public sealed record MarketDetailDto
{
    public string Symbol { get; init; } = string.Empty;
    public string ExchangeSymbol { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string MarketType { get; init; } = string.Empty;
    public string Timeframe { get; init; } = string.Empty;
    public string InitializationStatus { get; init; } = string.Empty;
    public string MonitoringStatus { get; init; } = string.Empty;
    public bool IsInitialized { get; init; }
    public bool IsActive { get; init; }
    public int? Rank { get; init; }
    public decimal? QuoteVolume24h { get; init; }
    public decimal? LastPrice { get; init; }
    public string? ContractStatus { get; init; }
    public DateTimeOffset DiscoveredAt { get; init; }
    public DateTimeOffset? LastTickerUpdate { get; init; }
    public DateTimeOffset? LastProcessedCandleTime { get; init; }
    public DateTimeOffset? LastEvaluationTime { get; init; }
    public DateTimeOffset? LastReceivedDataTime { get; init; }
    public string? TrendDirection { get; init; }
    public string? TrendCondition { get; init; }
    public DateTimeOffset? TrendEstablishedAt { get; init; }
    public int ConfirmedRetestCount { get; init; }
    public decimal? UpperSlope { get; init; }
    public decimal? LowerSlope { get; init; }
    public decimal? UpperBoundaryPrice { get; init; }
    public decimal? LowerBoundaryPrice { get; init; }
    public decimal? Atr { get; init; }
    public TimeSpan? TrendAge { get; init; }
    public int BoundaryRetestCount { get; init; }
    public bool UpperBoundaryBroken { get; init; }
    public bool LowerBoundaryBroken { get; init; }
    public string? LastError { get; init; }
    public int ConsecutiveErrors { get; init; }
    public DateTimeOffset? LastEvaluatedCandle { get; init; }
}