using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.Models;

namespace TradingScanner.Infrastructure.Services;

public sealed class HistoricalCandleLoader
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly CandleValidator _validator;
    private readonly TimeProvider _timeProvider;

    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(14);
    private const int MaxCandlesPerRequest = 200;

    public HistoricalCandleLoader(
        IMarketDataProvider marketDataProvider,
        CandleValidator validator,
        TimeProvider? timeProvider = null)
    {
        _marketDataProvider = marketDataProvider;
        _validator = validator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<CandleHistory> LoadLatestAsync(
        string symbol,
        Timeframe? timeframe = null,
        CancellationToken cancellationToken = default)
    {
        var tf = timeframe ?? Timeframe.FourHours;
        var now = _timeProvider.GetUtcNow();
        var latestClosedTime = GetLatestClosedCandleStart(now, tf.Interval);
        var defaultCandleCount = (int)(DefaultWindow.Ticks / tf.Interval.Ticks);
        var oldestTime = latestClosedTime -
            TimeSpan.FromTicks(tf.Interval.Ticks * (defaultCandleCount - 1));

        return LoadRangeAsync(symbol, tf, oldestTime, latestClosedTime, cancellationToken);
    }

    public async Task<CandleHistory> LoadRangeAsync(
        string symbol,
        Timeframe timeframe,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(timeframe);

        var now = _timeProvider.GetUtcNow();
        var interval = timeframe.Interval;
        var latestClosedTime = GetLatestClosedCandleStart(now, interval);

        var oldestTime = AlignToIntervalStart(startTime, interval);
        var latestTime = AlignToIntervalStart(endTime, interval);

        if (latestTime > latestClosedTime)
        {
            latestTime = latestClosedTime;
        }

        if (oldestTime > latestTime)
        {
            throw new InvalidOperationException(
                $"Invalid historical range: start time ({oldestTime:O}) is after end time ({latestTime:O}).");
        }

        var expectedCount = (int)((latestTime - oldestTime).Ticks / interval.Ticks) + 1;

        Console.WriteLine($"Current UTC time: {now:O}");
        Console.WriteLine($"Oldest required:  {oldestTime:O}");
        Console.WriteLine($"Latest required:  {latestTime:O}");
        Console.WriteLine($"Expected candles: {expectedCount}");

        var allCandles = new List<Candle>(expectedCount);
        var chunkSpan = TimeSpan.FromTicks(interval.Ticks * (MaxCandlesPerRequest - 1));

        for (var chunkStart = oldestTime;
             chunkStart <= latestTime;
             chunkStart = chunkStart.Add(TimeSpan.FromTicks(interval.Ticks * MaxCandlesPerRequest)))
        {
            var chunkEnd = chunkStart.Add(chunkSpan);
            if (chunkEnd > latestTime)
            {
                chunkEnd = latestTime;
            }

            var request = MarketDataRequest.ForHistoricalWindow(
                symbol,
                timeframe,
                chunkStart,
                chunkEnd,
                MaxCandlesPerRequest);

            var chunkCandles = await _marketDataProvider.GetCandlesAsync(
                request,
                cancellationToken);

            allCandles.AddRange(chunkCandles);
        }

        var normalizedCandles = allCandles
            .Where(c =>
                c.StartTime >= oldestTime &&
                c.StartTime <= latestTime &&
                c.StartTime.Add(interval) <= now)
            .ToList();

        var uniqueCandles =
            _validator.RemoveDuplicates(normalizedCandles);

        foreach (var candle in uniqueCandles)
        {
            if (!string.Equals(
                    candle.Symbol,
                    symbol,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Unexpected symbol: {candle.Symbol}");
            }

            _validator.ValidateCandle(candle, interval);
        }

        var missingCandles = _validator.FindMissingCandles(
            uniqueCandles,
            oldestTime,
            latestTime,
            interval);

        foreach (var missingTime in missingCandles)
        {
            Console.WriteLine(
                $"Recovering missing candle: {missingTime:O}");

            var recoveryStart = missingTime;
            var recoveryEnd = missingTime.Add(interval);

            var recoveryRequest = MarketDataRequest.ForHistoricalWindow(
                symbol,
                timeframe,
                recoveryStart,
                recoveryEnd,
                limit: 1);

            var recovered = await _marketDataProvider.GetCandlesAsync(
                recoveryRequest,
                cancellationToken);

            var matchingCandles = recovered
                .Where(c => c.StartTime == missingTime)
                .ToList();

            foreach (var recoveredCandle in matchingCandles)
            {
                _validator.ValidateCandle(recoveredCandle, interval);
            }

            uniqueCandles = _validator.RemoveDuplicates(
                uniqueCandles.Concat(matchingCandles));
        }

        missingCandles = _validator.FindMissingCandles(
            uniqueCandles,
            oldestTime,
            latestTime,
            interval);

        if (missingCandles.Count > 0)
        {
            var missingTimes = string.Join(
                ", ",
                missingCandles.Select(t => t.ToString("O")));

            throw new InvalidOperationException(
                $"Historical data is incomplete. Missing candles: " +
                missingTimes);
        }

        _validator.ValidateContinuity(
            uniqueCandles,
            oldestTime,
            latestTime,
            expectedCount,
            interval);

        Console.WriteLine("Historical candle validation successful.");

        return new CandleHistory(
            Symbol: symbol,
            Timeframe: timeframe.Name.ToUpperInvariant(),
            LoadedAt: now,
            OldestCandleTime: oldestTime,
            LatestCandleTime: latestTime,
            Candles: uniqueCandles);
    }

    private static DateTimeOffset AlignToIntervalStart(
        DateTimeOffset timestamp,
        TimeSpan interval)
    {
        var intervalMs = (long)interval.TotalMilliseconds;
        var alignedMs = (timestamp.ToUnixTimeMilliseconds() / intervalMs) * intervalMs;
        return DateTimeOffset.FromUnixTimeMilliseconds(alignedMs);
    }

    private static DateTimeOffset GetLatestClosedCandleStart(
        DateTimeOffset now,
        TimeSpan interval)
    {
        return AlignToIntervalStart(now, interval) - interval;
    }
}
