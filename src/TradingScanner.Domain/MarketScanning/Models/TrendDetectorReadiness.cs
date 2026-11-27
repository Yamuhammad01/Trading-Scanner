using TradingScanner.Domain.TrendDetection.Abstractions;

namespace TradingScanner.Domain.MarketScanning.Models;

public sealed class TrendDetectorReadiness
{
    public bool IsReady { get; }
    public string? Reason { get; }
    public ITrendDetector? Detector { get; }

    private TrendDetectorReadiness(bool isReady, ITrendDetector? detector, string? reason)
    {
        IsReady = isReady;
        Detector = detector;
        Reason = reason;
    }

    public static TrendDetectorReadiness Ready(ITrendDetector detector) =>
        new(true, detector ?? throw new ArgumentNullException(nameof(detector)), null);

    public static TrendDetectorReadiness NotReady(string reason) =>
        new(false, null, reason);
}
