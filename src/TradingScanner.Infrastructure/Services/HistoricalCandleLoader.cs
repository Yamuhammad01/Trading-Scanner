using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.Models;

namespace TradingScanner.Infrastructure.Services;

public sealed class HistoricalCandleLoader
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly CandleValidator _validator;

    private static readonly TimeSpan CandleInterval =
        TimeSpan.FromHours(4);

    private const int DefaultCandleCount = 84;

    public HistoricalCandleLoader(
        IMarketDataProvider marketDataProvider,
        CandleValidator validator)
    {
        _marketDataProvider = marketDataProvider;
        _validator = validator;
    }

    public async Task<CandleHistory> LoadLatestAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();

        var intervalMilliseconds =
            (long)CandleInterval.TotalMilliseconds;

        var currentCandleStartMilliseconds =
            (now.ToUnixTimeMilliseconds() / intervalMilliseconds)
            * intervalMilliseconds;

        var currentCandleStart =
            DateTimeOffset.FromUnixTimeMilliseconds(
                currentCandleStartMilliseconds);

        var latestClosedTime =
            currentCandleStart - CandleInterval;

        var oldestTime =
            latestClosedTime -
            TimeSpan.FromTicks(
                CandleInterval.Ticks * (DefaultCandleCount - 1));

        Console.WriteLine($"Current UTC time: {now:O}");
        Console.WriteLine($"Oldest required:  {oldestTime:O}");
        Console.WriteLine($"Latest required:  {latestClosedTime:O}");

        var request = MarketDataRequest.ForHistoricalWindow(
            symbol,
            oldestTime,
            latestClosedTime);

        var candles = await _marketDataProvider.GetCandlesAsync(
            request,
            cancellationToken);

        var normalizedCandles = candles
            .Where(c =>
                c.StartTime >= oldestTime &&
                c.StartTime <= latestClosedTime &&
                c.StartTime.Add(CandleInterval) <= now)
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

            _validator.ValidateCandle(candle);
        }

        var missingCandles = _validator.FindMissingCandles(
            uniqueCandles,
            oldestTime,
            latestClosedTime);

        foreach (var missingTime in missingCandles)
        {
            Console.WriteLine(
                $"Recovering missing candle: {missingTime:O}");

            var recoveryStart = missingTime;
            var recoveryEnd = missingTime.Add(CandleInterval);

            var recoveryRequest = MarketDataRequest.ForHistoricalWindow(
                symbol,
                recoveryStart,
                recoveryEnd);

            var recovered = await _marketDataProvider.GetCandlesAsync(
                recoveryRequest,
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

        _validator.ValidateContinuity(
            uniqueCandles,
            oldestTime,
            latestClosedTime,
            DefaultCandleCount);

        Console.WriteLine("Historical candle validation successful.");

        return new CandleHistory(
            Symbol: symbol,
            Timeframe: "4H",
            LoadedAt: now,
            OldestCandleTime: oldestTime,
            LatestCandleTime: latestClosedTime,
            Candles: uniqueCandles);
    }
}
