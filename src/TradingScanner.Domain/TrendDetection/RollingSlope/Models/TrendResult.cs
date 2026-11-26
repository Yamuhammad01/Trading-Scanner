namespace TradingScanner.Domain.TrendDetection.RollingSlope.Models;

public sealed record TrendResult
{
    public required string Provider { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }

    public TrendDirection Direction { get; init; }
    public TrendCondition Condition { get; init; }

    public decimal? UpperSlope { get; init; }
    public decimal? LowerSlope { get; init; }

    public decimal? UpperNormalizedSlope { get; init; }
    public decimal? LowerNormalizedSlope { get; init; }

    public decimal? UpperBoundaryPrice { get; init; }
    public decimal? LowerBoundaryPrice { get; init; }

    public decimal? Atr { get; init; }

    public DateTimeOffset? EstablishedAt { get; init; }

    public int BoundaryRetestCount { get; init; }

    public bool SignificantOppositeBoundaryBreakout { get; init; }

    public bool LowerBoundaryBroken { get; init; }
    public bool UpperBoundaryBroken { get; init; }

    public decimal? BreakoutDistanceAtr { get; init; }

    public TimeSpan? TrendAge { get; init; }

    public string? DiagnosticReason { get; init; }

    public DateTimeOffset EvaluatedAt { get; init; }

    public DateTimeOffset LastProcessedCandleTime { get; init; }
}
