using TradingScanner.Domain.SwingDetection.Models;

namespace TradingScanner.Domain.MarketStructure.Models;

public sealed record MarketStructurePoint(
    SwingPoint SwingPoint,
    StructureLabel Label
)
{
    public string Symbol => SwingPoint.Symbol;

    public string Timeframe => SwingPoint.Timeframe;

    public SwingPointType Type => SwingPoint.Type;

    public decimal Price => SwingPoint.Price;

    public DateTimeOffset OccurredAt => SwingPoint.OccurredAt;

    public DateTimeOffset ConfirmedAt => SwingPoint.ConfirmedAt;
}