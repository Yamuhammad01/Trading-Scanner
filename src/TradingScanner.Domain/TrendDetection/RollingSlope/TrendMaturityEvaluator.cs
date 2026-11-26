namespace TradingScanner.Domain.TrendDetection.RollingSlope;

public sealed class TrendMaturityEvaluator
{
    public bool IsMature(
        DateTimeOffset establishedAt,
        DateTimeOffset evaluationTime,
        int boundaryRetestCount,
        int minimumDurationDays,
        int minimumConfirmedRetests)
    {
        var elapsed = evaluationTime - establishedAt;
        return elapsed >= TimeSpan.FromDays(minimumDurationDays) &&
               boundaryRetestCount >= minimumConfirmedRetests;
    }
}
