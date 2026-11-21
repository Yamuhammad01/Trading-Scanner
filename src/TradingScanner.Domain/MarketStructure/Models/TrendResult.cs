namespace TradingScanner.Domain.MarketStructure.Models;

public sealed record TrendResult(
    string Symbol,
    string Timeframe,
    TrendDirection Direction,
    int HigherHighTransitions,
    int HigherLowTransitions,
    int LowerHighTransitions,
    int LowerLowTransitions,
    DateTimeOffset? EstablishedAt,
    IReadOnlyList<MarketStructurePoint> StructurePoints
);