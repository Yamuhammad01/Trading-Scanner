namespace TradingScanner.Domain.TrendDetection.RollingSlope.Models;

public sealed record TrendState
{
    public required string Provider { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required string ConfigurationId { get; init; }

    public TrendDirection Direction { get; init; } = TrendDirection.Neutral;
    public TrendCondition Condition { get; init; } = TrendCondition.NoValidTrend;

    public DateTimeOffset? EstablishedAt { get; init; }
    public int BoundaryRetestCount { get; init; }
    public bool IsInRetestInteraction { get; init; }

    public DateTimeOffset? LastProcessedCandleTime { get; init; }
    public BreakoutEvent? LastSignificantBreakout { get; init; }

    public bool IsChannelValid { get; init; }
    public decimal? LastUpperSlope { get; init; }
    public decimal? LastLowerSlope { get; init; }
    public decimal? LastUpperNormalizedSlope { get; init; }
    public decimal? LastLowerNormalizedSlope { get; init; }
    public decimal? LastUpperBoundaryPrice { get; init; }
    public decimal? LastLowerBoundaryPrice { get; init; }
    public decimal? LastAtr { get; init; }

    public static TrendState CreateInitial(
        string provider,
        string symbol,
        string timeframe,
        string configurationId) =>
        new()
        {
            Provider = provider,
            Symbol = symbol,
            Timeframe = timeframe,
            ConfigurationId = configurationId
        };
}
