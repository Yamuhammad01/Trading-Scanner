using TradingScanner.Domain.Entities;
using TradingScanner.Domain.TrendDetection.Configuration;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Domain.TrendDetection.RollingSlope;

public sealed class TrendStateManager
{
    private readonly BoundaryInteractionEvaluator _interactionEvaluator;
    private readonly TrendMaturityEvaluator _maturityEvaluator;

    public TrendStateManager(
        BoundaryInteractionEvaluator interactionEvaluator,
        TrendMaturityEvaluator maturityEvaluator)
    {
        _interactionEvaluator = interactionEvaluator;
        _maturityEvaluator = maturityEvaluator;
    }

    public TrendEvaluation Transition(
        TrendState previousState,
        Candle evaluationCandle,
        TrendChannel channel,
        RollingSlopeOptions options,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(previousState);
        ArgumentNullException.ThrowIfNull(evaluationCandle);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(options);

        var thresholdAtr = options.UpperBreakoutThresholdAtr
            ?? throw new InvalidOperationException("UpperBreakoutThresholdAtr is not configured.");

        var isContinuingDirection =
            previousState.Direction != TrendDirection.Neutral &&
            previousState.Direction == channel.Direction &&
            channel.IsValid;

        var wasInRetest = isContinuingDirection && previousState.IsInRetestInteraction;

        var interaction = _interactionEvaluator.Evaluate(
            evaluationCandle,
            channel,
            previousState.Direction,
            wasInRetest,
            options.TouchToleranceAtr,
            thresholdAtr);

        // Priority 1: Significant opposite-boundary breakout invalidates existing trend.
        if (interaction.SignificantOppositeBoundaryBreakout)
        {
            var breakoutEvent = new BreakoutEvent(
                OccurredAt: evaluationCandle.StartTime,
                InvalidatedDirection: previousState.Direction,
                ClosePrice: evaluationCandle.Close,
                BoundaryPrice: previousState.Direction == TrendDirection.Bearish
                    ? channel.Upper.ProjectedPrice
                    : channel.Lower.ProjectedPrice,
                BreakoutDistanceAtr: interaction.BreakoutDistanceAtr!.Value);

            var invalidatedState = previousState with
            {
                Direction = TrendDirection.Neutral,
                Condition = TrendCondition.PotentialReversal,
                EstablishedAt = null,
                BoundaryRetestCount = 0,
                IsInRetestInteraction = false,
                LastProcessedCandleTime = evaluationCandle.StartTime,
                LastSignificantBreakout = breakoutEvent,
                IsChannelValid = false,
                LastUpperSlope = channel.Upper.Slope,
                LastLowerSlope = channel.Lower.Slope,
                LastUpperNormalizedSlope = channel.Upper.NormalizedSlope,
                LastLowerNormalizedSlope = channel.Lower.NormalizedSlope,
                LastUpperBoundaryPrice = channel.Upper.ProjectedPrice,
                LastLowerBoundaryPrice = channel.Lower.ProjectedPrice,
                LastAtr = channel.Atr
            };

            var invalidatedResult = BuildResult(
                invalidatedState,
                channel,
                interaction,
                evaluatedAt,
                evaluationCandle.StartTime,
                $"Significant opposite-boundary breakout invalidated {previousState.Direction} trend.");

            return new TrendEvaluation(invalidatedResult, invalidatedState);
        }

        // Priority 2: Check directional channel validity.
        if (!channel.IsValid || channel.Direction == TrendDirection.Neutral)
        {
            var neutralState = previousState with
            {
                Direction = TrendDirection.Neutral,
                Condition = TrendCondition.NoValidTrend,
                EstablishedAt = null,
                BoundaryRetestCount = 0,
                IsInRetestInteraction = false,
                LastProcessedCandleTime = evaluationCandle.StartTime,
                IsChannelValid = false,
                LastUpperSlope = channel.Upper.Slope,
                LastLowerSlope = channel.Lower.Slope,
                LastUpperNormalizedSlope = channel.Upper.NormalizedSlope,
                LastLowerNormalizedSlope = channel.Lower.NormalizedSlope,
                LastUpperBoundaryPrice = channel.Upper.ProjectedPrice,
                LastLowerBoundaryPrice = channel.Lower.ProjectedPrice,
                LastAtr = channel.Atr
            };

            var neutralResult = BuildResult(
                neutralState,
                channel,
                interaction,
                evaluatedAt,
                evaluationCandle.StartTime,
                channel.InvalidReason ?? "No valid directional channel.");

            return new TrendEvaluation(neutralResult, neutralState);
        }

        // Valid directional channel exists.
        var establishedAt = isContinuingDirection
            ? previousState.EstablishedAt ?? evaluationCandle.StartTime
            : evaluationCandle.StartTime;

        var retestCount = isContinuingDirection
            ? previousState.BoundaryRetestCount
            : 0;

        if (interaction.ConfirmedIndependentRetest)
        {
            retestCount++;
        }

        // Priority 3-5: Acceleration -> Mature -> Established -> Developing.
        TrendCondition condition;
        var isAccelerating =
            (channel.Direction == TrendDirection.Bearish && interaction.LowerBoundaryBroken) ||
            (channel.Direction == TrendDirection.Bullish && interaction.UpperBoundaryBroken);

        if (isAccelerating)
        {
            condition = TrendCondition.Accelerating;
        }
        else if (_maturityEvaluator.IsMature(
                     establishedAt,
                     evaluationCandle.StartTime,
                     retestCount,
                     options.MinimumDurationDays,
                     options.MinimumConfirmedBoundaryRetests))
        {
            condition = TrendCondition.Mature;
        }
        else if (retestCount >= options.MinimumConfirmedBoundaryRetests)
        {
            condition = TrendCondition.Established;
        }
        else
        {
            condition = TrendCondition.Developing;
        }

        var updatedState = previousState with
        {
            Direction = channel.Direction,
            Condition = condition,
            EstablishedAt = establishedAt,
            BoundaryRetestCount = retestCount,
            IsInRetestInteraction = interaction.IsInRetestInteraction,
            LastProcessedCandleTime = evaluationCandle.StartTime,
            IsChannelValid = true,
            LastUpperSlope = channel.Upper.Slope,
            LastLowerSlope = channel.Lower.Slope,
            LastUpperNormalizedSlope = channel.Upper.NormalizedSlope,
            LastLowerNormalizedSlope = channel.Lower.NormalizedSlope,
            LastUpperBoundaryPrice = channel.Upper.ProjectedPrice,
            LastLowerBoundaryPrice = channel.Lower.ProjectedPrice,
            LastAtr = channel.Atr
        };

        var result = BuildResult(
            updatedState,
            channel,
            interaction,
            evaluatedAt,
            evaluationCandle.StartTime,
            diagnosticReason: null);

        return new TrendEvaluation(result, updatedState);
    }

    private static TrendResult BuildResult(
        TrendState state,
        TrendChannel channel,
        BoundaryInteractionResult interaction,
        DateTimeOffset evaluatedAt,
        DateTimeOffset candleTime,
        string? diagnosticReason)
    {
        TimeSpan? trendAge = state.EstablishedAt.HasValue
            ? candleTime - state.EstablishedAt.Value
            : null;

        return new TrendResult
        {
            Provider = state.Provider,
            Symbol = state.Symbol,
            Timeframe = state.Timeframe,
            Direction = state.Direction,
            Condition = state.Condition,
            UpperSlope = channel.Upper.Slope,
            LowerSlope = channel.Lower.Slope,
            UpperNormalizedSlope = channel.Upper.NormalizedSlope,
            LowerNormalizedSlope = channel.Lower.NormalizedSlope,
            UpperBoundaryPrice = channel.Upper.ProjectedPrice,
            LowerBoundaryPrice = channel.Lower.ProjectedPrice,
            Atr = channel.Atr,
            EstablishedAt = state.EstablishedAt,
            BoundaryRetestCount = state.BoundaryRetestCount,
            SignificantOppositeBoundaryBreakout = interaction.SignificantOppositeBoundaryBreakout,
            LowerBoundaryBroken = interaction.LowerBoundaryBroken,
            UpperBoundaryBroken = interaction.UpperBoundaryBroken,
            BreakoutDistanceAtr = interaction.BreakoutDistanceAtr,
            TrendAge = trendAge,
            DiagnosticReason = diagnosticReason,
            EvaluatedAt = evaluatedAt,
            LastProcessedCandleTime = candleTime
        };
    }
}
