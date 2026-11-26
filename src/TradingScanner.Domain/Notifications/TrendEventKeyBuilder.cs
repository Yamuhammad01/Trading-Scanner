using System.Globalization;
using MarketStructureTrendResult = TradingScanner.Domain.MarketStructure.Models.TrendResult;
using MarketStructureTrendDirection = TradingScanner.Domain.MarketStructure.Models.TrendDirection;
using RollingSlopeTrendResult = TradingScanner.Domain.TrendDetection.RollingSlope.Models.TrendResult;
using RollingSlopeTrendDirection = TradingScanner.Domain.TrendDetection.RollingSlope.Models.TrendDirection;

namespace TradingScanner.Domain.Notifications;

public static class TrendEventKeyBuilder
{
    public static string Build(MarketStructureTrendResult trend)
    {
        if (trend.Direction == MarketStructureTrendDirection.Neutral)
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

    public static string Build(RollingSlopeTrendResult trend)
    {
        if (trend.Direction == RollingSlopeTrendDirection.Neutral)
        {
            throw new ArgumentException(
                "Cannot build an alert key for a neutral trend.");
        }

        var established = trend.EstablishedAt?.ToUniversalTime().ToString("O") ?? "unknown";
        return $"{trend.Provider}|{trend.Symbol}|{trend.Timeframe}|" +
               $"{trend.Direction}|{trend.Condition}|{established}";
    }
}