using TradingScanner.Domain.MarketStructure.Models;

namespace TradingScanner.Domain.MarketStructure;

public sealed class TrendClassifier
{
    private const int RequiredTransitions = 3;

    public TrendResult Classify(
        IReadOnlyList<MarketStructurePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return CreateNeutralResult(
                "UNKNOWN",
                "UNKNOWN",
                points);
        }

        var symbol = points[0].Symbol;
        var timeframe = points[0].Timeframe;

        if (points.Count < 8)
        {
            return CreateNeutralResult(
                symbol,
                timeframe,
                points);
        }

        var recent = points
            .TakeLast(6)
            .ToList();

        var bullishPattern = new[]
        {
            StructureLabel.HH,
            StructureLabel.HL,
            StructureLabel.HH,
            StructureLabel.HL,
            StructureLabel.HH,
            StructureLabel.HL
        };

        var bearishPattern = new[]
        {
            StructureLabel.LH,
            StructureLabel.LL,
            StructureLabel.LH,
            StructureLabel.LL,
            StructureLabel.LH,
            StructureLabel.LL
        };

        var labels = recent
            .Select(x => x.Label)
            .ToArray();

        if (labels.SequenceEqual(bullishPattern))
        {
            return new TrendResult(
                Symbol: symbol,
                Timeframe: timeframe,
                Direction: TrendDirection.Bullish,
                HigherHighTransitions: RequiredTransitions,
                HigherLowTransitions: RequiredTransitions,
                LowerHighTransitions: 0,
                LowerLowTransitions: 0,
                EstablishedAt: recent[^1].ConfirmedAt,
                StructurePoints: recent
            );
        }

        if (labels.SequenceEqual(bearishPattern))
        {
            return new TrendResult(
                Symbol: symbol,
                Timeframe: timeframe,
                Direction: TrendDirection.Bearish,
                HigherHighTransitions: 0,
                HigherLowTransitions: 0,
                LowerHighTransitions: RequiredTransitions,
                LowerLowTransitions: RequiredTransitions,
                EstablishedAt: recent[^1].ConfirmedAt,
                StructurePoints: recent
            );
        }

        return CreateNeutralResult(
            symbol,
            timeframe,
            points);
    }

    private static TrendResult CreateNeutralResult(
        string symbol,
        string timeframe,
        IReadOnlyList<MarketStructurePoint> points)
    {
        return new TrendResult(
            Symbol: symbol,
            Timeframe: timeframe,
            Direction: TrendDirection.Neutral,
            HigherHighTransitions: 0,
            HigherLowTransitions: 0,
            LowerHighTransitions: 0,
            LowerLowTransitions: 0,
            EstablishedAt: null,
            StructurePoints: points
        );
    }
}