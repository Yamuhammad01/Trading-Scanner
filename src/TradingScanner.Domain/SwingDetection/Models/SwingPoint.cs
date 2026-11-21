namespace TradingScanner.Domain.SwingDetection.Models;

public enum SwingPointType
{
    SwingHigh,
    SwingLow
}

public sealed record SwingPoint(
    string Symbol,
    string Timeframe,
    SwingPointType Type,
    decimal Price,
    DateTimeOffset OccurredAt,
    DateTimeOffset ConfirmedAt
);