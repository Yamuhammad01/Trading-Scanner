using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Events;
using TradingScanner.Domain.MarketScanning.Models;
using TradingScanner.Infrastructure.MarketScanning.Monitoring;
using TradingScanner.Infrastructure.Services;

namespace TradingScanner.Infrastructure.MarketScanning.Recovery;

public sealed class RestReconciliationService
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly CandleValidator _validator;
    private readonly MarketCandleBuffer _candleBuffer;
    private readonly IMarketMonitoringStateRepository _monitoringStateRepository;
    private readonly MarketInitializationOptions _initOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RestReconciliationService> _logger;

    public RestReconciliationService(
        IMarketDataProvider marketDataProvider,
        CandleValidator validator,
        MarketCandleBuffer candleBuffer,
        IMarketMonitoringStateRepository monitoringStateRepository,
        MarketInitializationOptions initOptions,
        TimeProvider? timeProvider = null,
        ILogger<RestReconciliationService>? logger = null)
    {
        _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _candleBuffer = candleBuffer ?? throw new ArgumentNullException(nameof(candleBuffer));
        _monitoringStateRepository = monitoringStateRepository ?? throw new ArgumentNullException(nameof(monitoringStateRepository));
        _initOptions = initOptions ?? throw new ArgumentNullException(nameof(initOptions));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<RestReconciliationService>.Instance;
    }

    public async Task<IReadOnlyList<ClosedCandleEvent>> ReconcileAsync(
        MarketUniverseEntry market,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(market);

        var provider = _marketDataProvider.ProviderName;
        var timeframe = Timeframe.Parse(_initOptions.Timeframe);
        var tfName = timeframe.Name.ToUpperInvariant();
        var now = _timeProvider.GetUtcNow();
        var interval = timeframe.Interval;

        // Find the last processed time from monitoring state.
        var monitoringState = await _monitoringStateRepository.GetAsync(
            provider, market.CanonicalSymbol, tfName, cancellationToken);

        // Also check candle buffer for the latest candle time.
        var bufferLatest = _candleBuffer.GetLatestCandleTime(provider, market.CanonicalSymbol, tfName);
        var lastKnownTime = monitoringState?.LastProcessedCandleTime ?? bufferLatest;

        if (!lastKnownTime.HasValue)
        {
            _logger.LogDebug(
                "No previous data for {Symbol} {Timeframe}. Skipping reconciliation (should use initialization instead).",
                market.CanonicalSymbol, tfName);
            return [];
        }

        // Calculate the latest closed candle time.
        var latestClosed = AlignToIntervalStart(now, interval) - interval;

        // If we're caught up, nothing to do.
        if (lastKnownTime.Value >= latestClosed)
        {
            return [];
        }

        // Retrieve missed candles via REST.
        var startTime = lastKnownTime.Value + interval; // The first missed candle.
        var request = MarketDataRequest.ForHistoricalWindow(
            market.CanonicalSymbol,
            timeframe,
            startTime,
            latestClosed,
            limit: 200);

        _logger.LogInformation(
            "Reconciling {Symbol} {Timeframe}: {Start:O} to {End:O}",
            market.CanonicalSymbol, tfName, startTime, latestClosed);

        IReadOnlyList<Candle> rawCandles;
        try
        {
            rawCandles = await _marketDataProvider.GetCandlesAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "REST reconciliation failed for {Symbol}.", market.CanonicalSymbol);
            return [];
        }

        // Normalize, validate, and filter.
        var validCandles = rawCandles
            .Where(c => c.StartTime >= startTime && c.StartTime <= latestClosed)
            .Where(c => c.StartTime.Add(interval) <= now) // Ensure fully closed.
            .OrderBy(c => c.StartTime)
            .ToList();

        var uniqueCandles = _validator.RemoveDuplicates(validCandles);

        // Convert to ClosedCandleEvents.
        var events = new List<ClosedCandleEvent>(uniqueCandles.Count);
        foreach (var candle in uniqueCandles)
        {
            try
            {
                _validator.ValidateCandle(candle, interval);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid recovered candle for {Symbol} at {Time}. Skipping.",
                    market.CanonicalSymbol, candle.StartTime);
                continue;
            }

            events.Add(new ClosedCandleEvent(
                Provider: provider,
                CanonicalSymbol: market.CanonicalSymbol,
                NativeSymbol: market.NativeSymbol,
                Timeframe: tfName,
                CandleStartTime: candle.StartTime,
                Candle: candle,
                IsFromRecovery: true));
        }

        _logger.LogInformation(
            "Reconciliation recovered {Count} candles for {Symbol} {Timeframe}.",
            events.Count, market.CanonicalSymbol, tfName);

        return events;
    }

    private static DateTimeOffset AlignToIntervalStart(DateTimeOffset timestamp, TimeSpan interval)
    {
        var intervalMs = (long)interval.TotalMilliseconds;
        var alignedMs = (timestamp.ToUnixTimeMilliseconds() / intervalMs) * intervalMs;
        return DateTimeOffset.FromUnixTimeMilliseconds(alignedMs);
    }
}
