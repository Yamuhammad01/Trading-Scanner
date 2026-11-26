using TradingScanner.Domain.Entities;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Domain.TrendDetection.RollingSlope;

public readonly record struct BoundaryInteractionResult(
    bool SignificantOppositeBoundaryBreakout,
    decimal? BreakoutDistanceAtr,
    bool LowerBoundaryBroken,
    bool UpperBoundaryBroken,
    bool IsInRetestInteraction,
    bool ConfirmedIndependentRetest);

public sealed class BoundaryInteractionEvaluator
{
    public BoundaryInteractionResult Evaluate(
        Candle candle,
        TrendChannel channel,
        TrendDirection activeDirectionForBreakout,
        bool wasInRetestInteraction,
        decimal touchToleranceAtr,
        decimal upperBreakoutThresholdAtr)
    {
        ArgumentNullException.ThrowIfNull(candle);
        ArgumentNullException.ThrowIfNull(channel);

        var upperPrice = channel.Upper.ProjectedPrice;
        var lowerPrice = channel.Lower.ProjectedPrice;
        var atr = channel.Atr;

        if (upperPrice <= lowerPrice || atr <= 0m)
        {
            return new BoundaryInteractionResult(
                SignificantOppositeBoundaryBreakout: false,
                BreakoutDistanceAtr: null,
                LowerBoundaryBroken: false,
                UpperBoundaryBroken: false,
                IsInRetestInteraction: false,
                ConfirmedIndependentRetest: false);
        }

        // 1. Significant opposite-boundary breakout check against active trend direction.
        var significantBreakout = false;
        decimal? breakoutDistanceAtr = null;

        if (activeDirectionForBreakout == TrendDirection.Bearish &&
            candle.Close > upperPrice)
        {
            var distance = (candle.Close - upperPrice) / atr;
            breakoutDistanceAtr = distance;
            significantBreakout = distance >= upperBreakoutThresholdAtr;
        }
        else if (activeDirectionForBreakout == TrendDirection.Bullish &&
                 candle.Close < lowerPrice)
        {
            var distance = (lowerPrice - candle.Close) / atr;
            breakoutDistanceAtr = distance;
            significantBreakout = distance >= upperBreakoutThresholdAtr;
        }

        if (significantBreakout)
        {
            return new BoundaryInteractionResult(
                SignificantOppositeBoundaryBreakout: true,
                BreakoutDistanceAtr: breakoutDistanceAtr,
                LowerBoundaryBroken: activeDirectionForBreakout == TrendDirection.Bullish,
                UpperBoundaryBroken: activeDirectionForBreakout == TrendDirection.Bearish,
                IsInRetestInteraction: false,
                ConfirmedIndependentRetest: false);
        }

        if (!channel.IsValid || channel.Direction == TrendDirection.Neutral)
        {
            return new BoundaryInteractionResult(
                SignificantOppositeBoundaryBreakout: false,
                BreakoutDistanceAtr: breakoutDistanceAtr,
                LowerBoundaryBroken: false,
                UpperBoundaryBroken: false,
                IsInRetestInteraction: false,
                ConfirmedIndependentRetest: false);
        }

        // 2. Directional acceleration check.
        var lowerBroken = channel.Direction == TrendDirection.Bearish &&
                          candle.Close < lowerPrice;
        var upperBroken = channel.Direction == TrendDirection.Bullish &&
                          candle.Close > upperPrice;

        // 3. Boundary retest & confirmed rejection check.
        var tolerance = atr * touchToleranceAtr;
        var isInInteractionArea = false;
        var confirmedRejection = false;

        if (channel.Direction == TrendDirection.Bearish)
        {
            isInInteractionArea =
                candle.Low <= lowerPrice + tolerance &&
                candle.High >= lowerPrice - tolerance;

            confirmedRejection = isInInteractionArea && candle.Close > lowerPrice;
        }
        else if (channel.Direction == TrendDirection.Bullish)
        {
            isInInteractionArea =
                candle.High >= upperPrice - tolerance &&
                candle.Low <= upperPrice + tolerance;

            confirmedRejection = isInInteractionArea && candle.Close < upperPrice;
        }

        var confirmedIndependentRetest =
            confirmedRejection && !wasInRetestInteraction;

        return new BoundaryInteractionResult(
            SignificantOppositeBoundaryBreakout: false,
            BreakoutDistanceAtr: breakoutDistanceAtr,
            LowerBoundaryBroken: lowerBroken,
            UpperBoundaryBroken: upperBroken,
            IsInRetestInteraction: isInInteractionArea,
            ConfirmedIndependentRetest: confirmedIndependentRetest);
    }
}
