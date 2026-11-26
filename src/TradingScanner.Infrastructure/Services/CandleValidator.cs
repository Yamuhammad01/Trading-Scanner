using TradingScanner.Domain.Entities;

namespace TradingScanner.Infrastructure.Services;

public sealed class CandleValidator
{
    private static readonly TimeSpan DefaultCandleInterval =
        TimeSpan.FromHours(4);

    public IReadOnlyList<Candle> RemoveDuplicates(
        IEnumerable<Candle> candles)
    {
        var uniqueCandles = new Dictionary<long, Candle>();

        foreach (var candle in candles)
        {
            var timestamp = candle.StartTime.ToUnixTimeMilliseconds();

            if (uniqueCandles.TryGetValue(timestamp, out var existing))
            {
                if (existing != candle)
                {
                    throw new InvalidOperationException(
                        $"Conflicting duplicate candle detected at " +
                        $"{candle.StartTime:O}.");
                }

                // Identical duplicate: ignore it.
                continue;
            }

            uniqueCandles.Add(timestamp, candle);
        }

        return uniqueCandles.Values
            .OrderBy(c => c.StartTime)
            .ToList();
    }

    public void ValidateCandle(Candle candle) =>
        ValidateCandle(candle, DefaultCandleInterval);

    public void ValidateCandle(Candle candle, TimeSpan candleInterval)
    {
        var intervalMilliseconds = (long)candleInterval.TotalMilliseconds;
        var timestamp = candle.StartTime.ToUnixTimeMilliseconds();

        // Check that candle starts on a valid UTC boundary for the timeframe.

        if (timestamp % intervalMilliseconds != 0)
        {
            throw new InvalidOperationException(
                $"Invalid {candleInterval} candle timestamp: {candle.StartTime:O}");
        }

        // Check price values.

        if (candle.Open <= 0 ||
            candle.High <= 0 ||
            candle.Low <= 0 ||
            candle.Close <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid price value at {candle.StartTime:O}");
        }

        // High must be greater than or equal to Open and Close.

        if (candle.High < candle.Open ||
            candle.High < candle.Close)
        {
            throw new InvalidOperationException(
                $"Invalid candle high at {candle.StartTime:O}");
        }

        // Low must be less than or equal to Open and Close.

        if (candle.Low > candle.Open ||
            candle.Low > candle.Close)
        {
            throw new InvalidOperationException(
                $"Invalid candle low at {candle.StartTime:O}");
        }

        // Low cannot be greater than High.

        if (candle.Low > candle.High)
        {
            throw new InvalidOperationException(
                $"Candle low is greater than high at {candle.StartTime:O}");
        }

        if (candle.Volume < 0 || candle.Turnover < 0)
        {
            throw new InvalidOperationException(
                $"Negative volume or turnover at {candle.StartTime:O}");
        }
    }

    public List<DateTimeOffset> FindMissingCandles(
        IReadOnlyList<Candle> candles,
        DateTimeOffset oldestTime,
        DateTimeOffset latestTime) =>
        FindMissingCandles(candles, oldestTime, latestTime, DefaultCandleInterval);

    public List<DateTimeOffset> FindMissingCandles(
        IReadOnlyList<Candle> candles,
        DateTimeOffset oldestTime,
        DateTimeOffset latestTime,
        TimeSpan candleInterval)
    {
        var existingTimestamps = candles
            .Select(c => c.StartTime.ToUnixTimeMilliseconds())
            .ToHashSet();

        var missing = new List<DateTimeOffset>();

        for (var time = oldestTime;
             time <= latestTime;
             time = time.Add(candleInterval))
        {
            var timestamp = time.ToUnixTimeMilliseconds();

            if (!existingTimestamps.Contains(timestamp))
            {
                missing.Add(time);
            }
        }

        return missing;
    }

    public void ValidateContinuity(
        IReadOnlyList<Candle> candles,
        DateTimeOffset oldestTime,
        DateTimeOffset latestTime,
        int expectedCount) =>
        ValidateContinuity(candles, oldestTime, latestTime, expectedCount, DefaultCandleInterval);

    public void ValidateContinuity(
        IReadOnlyList<Candle> candles,
        DateTimeOffset oldestTime,
        DateTimeOffset latestTime,
        int expectedCount,
        TimeSpan candleInterval)
    {
        if (candles.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} candles, " +
                $"but received {candles.Count}.");
        }

        if (candles[0].StartTime != oldestTime)
        {
            throw new InvalidOperationException(
                "The oldest candle does not match the expected start time.");
        }

        if (candles[^1].StartTime != latestTime)
        {
            throw new InvalidOperationException(
                "The latest candle does not match the expected start time.");
        }

        for (var i = 1; i < candles.Count; i++)
        {
            var expectedNextTime =
                candles[i - 1].StartTime.Add(candleInterval);

            if (candles[i].StartTime != expectedNextTime)
            {
                throw new InvalidOperationException(
                    $"Missing or out-of-order candle between " +
                    $"{candles[i - 1].StartTime:O} and " +
                    $"{candles[i].StartTime:O}.");
            }
        }
    }
}