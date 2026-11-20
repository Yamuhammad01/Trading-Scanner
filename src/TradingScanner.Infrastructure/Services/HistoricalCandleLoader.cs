using TradingScanner.Domain.Entities;
using TradingScanner.Domain.Models;
using TradingScanner.Infrastructure.Bybit;

namespace TradingScanner.Infrastructure.Services;

public sealed class HistoricalCandleLoader
{
    private readonly BybitRestClient _bybitClient;
    private readonly CandleValidator _validator;
    private readonly TimeProvider _timeProvider;

    private static readonly TimeSpan CandleInterval =
        TimeSpan.FromHours(4);

    private const int DefaultCandleCount = 84;

    public HistoricalCandleLoader(
        BybitRestClient bybitClient,
        CandleValidator validator,
        TimeProvider timeProvider)
    {
        _bybitClient = bybitClient;
        _validator = validator;
        _timeProvider = timeProvider;
    }

    public async Task<CandleHistory> LoadLatestAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        const int candleCount = DefaultCandleCount;

        Console.WriteLine();
        Console.WriteLine("Preparing historical candle window...");

        // 1. Get current UTC time.

        var now = _timeProvider.GetUtcNow();

        // 2. Calculate the latest 4H candle boundary.

        var intervalMilliseconds =
            (long)CandleInterval.TotalMilliseconds;

        var currentCandleStartMilliseconds =
            (now.ToUnixTimeMilliseconds() / intervalMilliseconds)
            * intervalMilliseconds;

        var currentCandleStart =
            DateTimeOffset.FromUnixTimeMilliseconds(
                currentCandleStartMilliseconds);

        // 3. The previous candle is the latest fully closed candle.

        var latestClosedTime =
            currentCandleStart - CandleInterval;

        // 4. Calculate the oldest candle in our 84-candle window.

        var oldestTime =
            latestClosedTime -
            TimeSpan.FromTicks(
                CandleInterval.Ticks * (candleCount - 1));

        Console.WriteLine($"Current UTC time: {now:O}");
        Console.WriteLine($"Oldest required:  {oldestTime:O}");
        Console.WriteLine($"Latest required:  {latestClosedTime:O}");

        // 5. Retrieve the requested historical window.

        var candles = await _bybitClient.GetCandlesAsync(
            symbol,
            oldestTime,
            latestClosedTime,
            limit: 1000,
            cancellationToken);

        // 6. Keep only the expected time range.

        var normalizedCandles = candles
            .Where(c =>
                c.StartTime >= oldestTime &&
                c.StartTime <= latestClosedTime &&
                c.StartTime.Add(CandleInterval) <= now)
            .ToList();

        // 7. Remove identical duplicates and sort chronologically.

        var uniqueCandles =
            _validator.RemoveDuplicates(normalizedCandles);

        // 8. Validate each candle.

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

            _validator.ValidateCandle(candle);
        }

        // 9. Find missing 4H intervals.

        var missingCandles = _validator.FindMissingCandles(
            uniqueCandles,
            oldestTime,
            latestClosedTime);

        // 10. Attempt to recover missing candles.

        foreach (var missingTime in missingCandles)
        {
            Console.WriteLine(
                $"Recovering missing candle: {missingTime:O}");

            var recovered = await _bybitClient.GetCandlesAsync(
                symbol,
                missingTime,
                missingTime,
                limit: 10,
                cancellationToken);

            var matchingCandles = recovered
                .Where(c => c.StartTime == missingTime)
                .ToList();

            foreach (var recoveredCandle in matchingCandles)
            {
                _validator.ValidateCandle(recoveredCandle);
            }

            uniqueCandles = _validator.RemoveDuplicates(
                uniqueCandles.Concat(matchingCandles));
        }

        // 11. Recheck for missing candles.

        missingCandles = _validator.FindMissingCandles(
            uniqueCandles,
            oldestTime,
            latestClosedTime);

        if (missingCandles.Count > 0)
        {
            var missingTimes = string.Join(
                ", ",
                missingCandles.Select(t => t.ToString("O")));

            throw new InvalidOperationException(
                $"Historical data is incomplete. Missing candles: " +
                missingTimes);
        }

        // 12. Ensure exactly 84 consecutive candles exist.

        _validator.ValidateContinuity(
            uniqueCandles,
            oldestTime,
            latestClosedTime,
            candleCount);

        Console.WriteLine(
            "Historical candle validation successful.");

        // 13. Return the validated history.

        return new CandleHistory(
            Symbol: symbol,
            Timeframe: "4H",
            LoadedAt: now,
            OldestCandleTime: oldestTime,
            LatestCandleTime: latestClosedTime,
            Candles: uniqueCandles
        );
    }
}