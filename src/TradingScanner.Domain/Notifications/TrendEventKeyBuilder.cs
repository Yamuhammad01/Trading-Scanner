using System.Globalization;
using TradingScanner.Domain.MarketStructure.Models;

namespace TradingScanner.Domain.Notifications;

public static class TrendEventKeyBuilder
{
    public static string Build(TrendResult trend)
    {
        if (trend.Direction == TrendDirection.Neutral)
        {
            throw new ArgumentException(
                "Cannot build an alert key for a neutral trend.");
        }

        var swingIdentity = string.Join(
            ";",
            trend.StructurePoints.Select(point =>
                $"{point.Type}:{point.OccurredAt.ToUniversalTime():O}:" +
                $"{point.ConfirmedAt.ToUniversalTime():O}:" +
                $"{point.Price.ToString(CultureInfo.InvariantCulture)}"));

        return $"{trend.Symbol}|{trend.Timeframe}|" +
               $"{trend.Direction}|{swingIdentity}";
    }
}