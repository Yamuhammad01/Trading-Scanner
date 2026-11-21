using TradingScanner.Domain.MarketStructure.Models;
using TradingScanner.Domain.SwingDetection.Models;

namespace TradingScanner.Domain.MarketStructure;

public sealed class MarketStructureDetector
{
    public IReadOnlyList<MarketStructurePoint> Detect(
        IEnumerable<SwingPoint> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var swings = input
            .OrderBy(x => x.OccurredAt)
            .ToList();

        if (swings.Count == 0)
            return Array.Empty<MarketStructurePoint>();

        ValidateInput(swings);

        var result = new List<MarketStructurePoint>();

        SwingPoint? previousHigh = null;
        SwingPoint? previousLow = null;

        foreach (var swing in swings)
        {
            StructureLabel label;

            if (swing.Type == SwingPointType.SwingHigh)
            {
                if (previousHigh is null)
                {
                    label = StructureLabel.Baseline;
                }
                else if (swing.Price > previousHigh.Price)
                {
                    label = StructureLabel.HH;
                }
                else if (swing.Price < previousHigh.Price)
                {
                    label = StructureLabel.LH;
                }
                else
                {
                    throw new InvalidOperationException(
                        "Equal consecutive swing highs should not " +
                        "be classified as HH or LH. Check the " +
                        "swing detector's equal-high handling.");
                }

                previousHigh = swing;
            }
            else
            {
                if (previousLow is null)
                {
                    label = StructureLabel.Baseline;
                }
                else if (swing.Price > previousLow.Price)
                {
                    label = StructureLabel.HL;
                }
                else if (swing.Price < previousLow.Price)
                {
                    label = StructureLabel.LL;
                }
                else
                {
                    throw new InvalidOperationException(
                        "Equal consecutive swing lows should not " +
                        "be classified as HL or LL. Check the " +
                        "swing detector's equal-low handling.");
                }

                previousLow = swing;
            }

            result.Add(new MarketStructurePoint(
                SwingPoint: swing,
                Label: label
            ));
        }

        return result;
    }

    private static void ValidateInput(
        IReadOnlyList<SwingPoint> swings)
    {
        var first = swings[0];

        for (int i = 0; i < swings.Count; i++)
        {
            var current = swings[i];

            if (current.Symbol != first.Symbol ||
                current.Timeframe != first.Timeframe)
            {
                throw new InvalidOperationException(
                    "All swing points must belong to the same " +
                    "symbol and timeframe.");
            }

            if (current.Price <= 0)
            {
                throw new InvalidOperationException(
                    "Swing prices must be positive.");
            }

            if (current.ConfirmedAt < current.OccurredAt)
            {
                throw new InvalidOperationException(
                    "A swing cannot be confirmed before it occurs.");
            }

            if (i == 0)
                continue;

            var previous = swings[i - 1];

            if (current.OccurredAt == previous.OccurredAt)
            {
                throw new InvalidOperationException(
                    "Two swing points cannot have the same " +
                    "occurrence timestamp.");
            }

            if (current.Type == previous.Type)
            {
                throw new InvalidOperationException(
                    "Swing points must alternate between " +
                    "SwingHigh and SwingLow.");
            }
        }
    }
}