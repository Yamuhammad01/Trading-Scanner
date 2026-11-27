using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Domain.MarketScanning.Models;

public sealed record MarketMonitoringState
{
    public required string Provider { get; init; }
    public required string CanonicalSymbol { get; init; }
    public required string NativeSymbol { get; init; }
    public required string Timeframe { get; init; }

    public bool IsInitialized { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset? LastProcessedCandleTime { get; init; }
    public DateTimeOffset? LastEvaluationTime { get; init; }
    public DateTimeOffset? LastReceivedDataTime { get; init; }

    public TrendDirection TrendDirection { get; init; } = TrendDirection.Neutral;
    public TrendCondition TrendCondition { get; init; } = TrendCondition.NoValidTrend;
    public DateTimeOffset? TrendEstablishedAt { get; init; }
    public int ConfirmedRetestCount { get; init; }

    public string? LastError { get; init; }
    public int ConsecutiveErrors { get; init; }
}
