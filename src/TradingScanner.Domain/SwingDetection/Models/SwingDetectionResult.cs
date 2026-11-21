namespace TradingScanner.Domain.SwingDetection.Models;

public sealed record SwingDetectionResult(
    IReadOnlyList<SwingPoint> SwingPoints,
    int ProcessedCandles,
    bool IsInitialized
);