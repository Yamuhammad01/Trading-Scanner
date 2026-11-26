namespace TradingScanner.Domain.TrendDetection.RollingSlope.Models;

public enum TrendCondition
{
    NoValidTrend,
    Developing,
    Established,
    Mature,
    Accelerating,
    PotentialReversal
}
