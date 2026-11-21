using TradingScanner.Domain.Entities;
using TradingScanner.Domain.SwingDetection.Models;

namespace TradingScanner.Domain.SwingDetection;

public sealed class SwingPointDetector
{
    private readonly SwingPointDetectorOptions _options;

    public SwingPointDetector(SwingPointDetectorOptions options)
    {
        if (options.ConfirmationCandles != 2)
        {
            throw new ArgumentException(
                "V1 currently supports exactly two confirmation candles.");
        }

        if (options.CandleInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Candle interval must be greater than zero.");
        }

        _options = options;
    }

    public SwingDetectionResult Detect(IEnumerable<Candle> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var candles = PrepareCandles(input);

        if (candles.Count < 3)
        {
            return new SwingDetectionResult(
                Array.Empty<SwingPoint>(),
                candles.Count,
                false);
        }

        var swingPoints = new List<SwingPoint>();

        var bootstrap = FindInitialSwing(candles);

        if (bootstrap is null)
        {
            return new SwingDetectionResult(
                swingPoints,
                candles.Count,
                false);
        }

        swingPoints.Add(bootstrap.Point);

        var currentSwing = bootstrap.Point;
        var searchFrom = bootstrap.OccurrenceIndex + 1;

        while (searchFrom < candles.Count)
        {
            var next = FindNextSwing(
                candles,
                currentSwing,
                searchFrom);

            if (next is null)
                break;

            if (next.Point.OccurredAt <= currentSwing.OccurredAt)
                throw new InvalidOperationException(
                    "Swing occurrence timestamps must alternate.");

            swingPoints.Add(next.Point);

            currentSwing = next.Point;
            searchFrom = next.OccurrenceIndex + 1;
        }

        return new SwingDetectionResult(
            swingPoints.OrderBy(x => x.OccurredAt).ToList(),
            candles.Count,
            true);
    }

    private List<Candle> PrepareCandles(IEnumerable<Candle> input)
    {
        var ordered = input
            .OrderBy(x => x.StartTime)
            .ToList();

        var result = new List<Candle>();

        foreach (var candle in ordered)
        {
            ValidateCandle(candle);

            if (result.Count == 0)
            {
                result.Add(candle);
                continue;
            }

            var previous = result[^1];

            if (candle.StartTime == previous.StartTime)
            {
                if (candle != previous)
                {
                    throw new InvalidOperationException(
                        $"Conflicting candles found at {candle.StartTime}.");
                }

                continue;
            }

            if (candle.StartTime - previous.StartTime
                != _options.CandleInterval)
            {
                throw new InvalidOperationException(
                    $"Missing or irregular candle between " +
                    $"{previous.StartTime} and {candle.StartTime}.");
            }

            result.Add(candle);
        }

        return result;
    }

    private static void ValidateCandle(Candle candle)
    {
        if (candle.High < candle.Low ||
            candle.High < candle.Open ||
            candle.High < candle.Close ||
            candle.Low > candle.Open ||
            candle.Low > candle.Close ||
            candle.Open <= 0 ||
            candle.Close <= 0 ||
            candle.High <= 0 ||
            candle.Low <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid OHLC values at {candle.StartTime}.");
        }
    }

    private Candidate? FindInitialSwing(List<Candle> candles)
    {
        decimal lowestLow = candles[0].Low;
        decimal highestHigh = candles[0].High;

        int lowestLowIndex = 0;
        int highestHighIndex = 0;

        int bullishCount = 0;
        int bearishCount = 0;

        Candidate? initialSwing = null;

        for (int i = 0; i < candles.Count; i++)
        {
            var candle = candles[i];

            bool bullish = IsBullish(candle);
            bool bearish = IsBearish(candle);

            if (candle.Low < lowestLow)
            {
                lowestLow = candle.Low;
                lowestLowIndex = i;

                bullishCount = bullish ? 1 : 0;
            }
            else
            {
                bullishCount = bullish ? bullishCount + 1 : 0;
            }

            if (candle.High > highestHigh)
            {
                highestHigh = candle.High;
                highestHighIndex = i;

                bearishCount = bearish ? 1 : 0;
            }
            else
            {
                bearishCount = bearish ? bearishCount + 1 : 0;
            }

            if (bullishCount == 2)
            {
                initialSwing = CreateCandidate(
                    candles,
                    lowestLowIndex,
                    i,
                    SwingPointType.SwingLow,
                    lowestLow);

                break;
            }

            if (bearishCount == 2)
            {
                initialSwing = CreateCandidate(
                    candles,
                    highestHighIndex,
                    i,
                    SwingPointType.SwingHigh,
                    highestHigh);

                break;
            }
        }

        return initialSwing;
    }

    private Candidate? FindNextSwing(
        List<Candle> candles,
        SwingPoint originatingSwing,
        int startIndex)
    {
        bool lookingForLow =
            originatingSwing.Type == SwingPointType.SwingHigh;

        decimal extreme = lookingForLow
            ? decimal.MaxValue
            : decimal.MinValue;

        int extremeIndex = -1;
        int confirmationCount = 0;

        for (int i = startIndex; i < candles.Count; i++)
        {
            var candle = candles[i];

            bool invalidated = lookingForLow
                ? candle.Close > originatingSwing.Price
                : candle.Close < originatingSwing.Price;

            if (invalidated)
            {
                return null;
            }

            bool newExtreme = lookingForLow
                ? candle.Low < extreme
                : candle.High > extreme;

            bool confirmingCandle = lookingForLow
                ? IsBullish(candle)
                : IsBearish(candle);

            if (newExtreme)
            {
                extreme = lookingForLow
                    ? candle.Low
                    : candle.High;

                extremeIndex = i;

                confirmationCount = confirmingCandle ? 1 : 0;
            }
            else
            {
                confirmationCount = confirmingCandle
                    ? confirmationCount + 1
                    : 0;
            }

            if (confirmationCount == _options.ConfirmationCandles)
            {
                return CreateCandidate(
                    candles,
                    extremeIndex,
                    i,
                    lookingForLow
                        ? SwingPointType.SwingLow
                        : SwingPointType.SwingHigh,
                    extreme);
            }
        }

        return null;
    }

    private Candidate CreateCandidate(
        List<Candle> candles,
        int occurrenceIndex,
        int confirmationIndex,
        SwingPointType type,
        decimal price)
    {
        var occurrenceCandle = candles[occurrenceIndex];
        var confirmationCandle = candles[confirmationIndex];

        var point = new SwingPoint(
            Symbol: occurrenceCandle.Symbol,
            Timeframe: _options.Timeframe,
            Type: type,
            Price: price,
            OccurredAt: occurrenceCandle.StartTime,
            ConfirmedAt: confirmationCandle.StartTime
                + _options.CandleInterval
        );

        return new Candidate(point, occurrenceIndex);
    }

    private static bool IsBullish(Candle candle) =>
        candle.Close > candle.Open;

    private static bool IsBearish(Candle candle) =>
        candle.Close < candle.Open;

    private sealed record Candidate(
        SwingPoint Point,
        int OccurrenceIndex
    );
}