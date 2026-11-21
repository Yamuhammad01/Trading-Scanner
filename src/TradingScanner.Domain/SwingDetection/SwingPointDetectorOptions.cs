namespace TradingScanner.Domain.SwingDetection;

public sealed class SwingPointDetectorOptions
{
    public required string Timeframe { get; init; }

    public required TimeSpan CandleInterval { get; init; }

    public int ConfirmationCandles { get; init; } = 2;
}