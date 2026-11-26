namespace TradingScanner.Domain.TrendDetection.RollingSlope.Models;

public sealed record BreakoutEvent(
    DateTimeOffset OccurredAt,
    TrendDirection InvalidatedDirection,
    decimal ClosePrice,
    decimal BoundaryPrice,
    decimal BreakoutDistanceAtr);
