using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Events;
using TradingScanner.Domain.MarketScanning.Models;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;
using TradingScanner.Infrastructure.MarketScanning.Monitoring;

namespace TradingScanner.Infrastructure.MarketScanning.Processing;

public sealed class ClosedCandleProcessor
{
    private readonly MarketCandleBuffer _candleBuffer;
    private readonly TrendDetectorReadiness _trendReadiness;
    private readonly IMarketMonitoringStateRepository _monitoringStateRepository;
    private readonly ITrendStateRepository _trendStateRepository;
    private readonly Channel<TrendUpdatedEvent> _trendUpdatedChannel;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClosedCandleProcessor> _logger;

    public ClosedCandleProcessor(
        MarketCandleBuffer candleBuffer,
        TrendDetectorReadiness trendReadiness,
        IMarketMonitoringStateRepository monitoringStateRepository,
        ITrendStateRepository trendStateRepository,
        Channel<TrendUpdatedEvent>? trendUpdatedChannel = null,
        TimeProvider? timeProvider = null,
        ILogger<ClosedCandleProcessor>? logger = null)
    {
        _candleBuffer = candleBuffer ?? throw new ArgumentNullException(nameof(candleBuffer));
        _trendReadiness = trendReadiness ?? throw new ArgumentNullException(nameof(trendReadiness));
        _monitoringStateRepository = monitoringStateRepository ?? throw new ArgumentNullException(nameof(monitoringStateRepository));
        _trendStateRepository = trendStateRepository ?? throw new ArgumentNullException(nameof(trendStateRepository));
        _trendUpdatedChannel = trendUpdatedChannel ?? Channel.CreateBounded<TrendUpdatedEvent>(
            new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ClosedCandleProcessor>.Instance;
    }

    public ChannelReader<TrendUpdatedEvent> TrendUpdates => _trendUpdatedChannel.Reader;

    public async Task ProcessAsync(ClosedCandleEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var provider = evt.Provider;
        var symbol = evt.CanonicalSymbol;
        var timeframe = evt.Timeframe;

        // 1. Append the candle to the buffer (deduplication via timestamp).
        var appended = _candleBuffer.TryAppend(provider, symbol, timeframe, evt.Candle);
        if (!appended)
        {
            _logger.LogDebug(
                "Duplicate candle for {Symbol} {Timeframe} at {Time}. Skipping.",
                symbol, timeframe, evt.CandleStartTime);
            return;
        }

        // 2. Retrieve monitoring state to check if already processed.
        var monitoringState = await _monitoringStateRepository.GetAsync(
            provider, symbol, timeframe, cancellationToken);

        if (monitoringState is not null &&
            monitoringState.LastProcessedCandleTime.HasValue &&
            evt.CandleStartTime <= monitoringState.LastProcessedCandleTime.Value)
        {
            _logger.LogDebug(
                "Candle for {Symbol} {Timeframe} at {Time} already processed. Skipping.",
                symbol, timeframe, evt.CandleStartTime);
            return;
        }

        // 3. If trend detector is ready, evaluate.
        if (!_trendReadiness.IsReady || _trendReadiness.Detector is null)
        {
            // Update monitoring state without trend evaluation.
            var updatedState = (monitoringState ?? CreateInitialMonitoringState(evt)) with
            {
                LastProcessedCandleTime = evt.CandleStartTime,
                LastReceivedDataTime = _timeProvider.GetUtcNow()
            };
            await _monitoringStateRepository.SaveAsync(updatedState, cancellationToken);

            _logger.LogDebug(
                "Trend detector not ready. Updated monitoring state for {Symbol} {Timeframe}.",
                symbol, timeframe);
            return;
        }

        // 4. Get the full candle history snapshot for evaluation.
        var candles = _candleBuffer.GetSnapshot(provider, symbol, timeframe);
        if (candles.Count == 0)
        {
            _logger.LogWarning(
                "No candles in buffer for {Symbol} {Timeframe}. Cannot evaluate.",
                symbol, timeframe);
            return;
        }

        // 5. Evaluate trend.
        var previousDirection = monitoringState?.TrendDirection ?? TrendDirection.Neutral;
        var previousCondition = monitoringState?.TrendCondition ?? TrendCondition.NoValidTrend;

        var trendResult = await _trendReadiness.Detector.EvaluateAsync(
            provider, symbol, timeframe, candles, cancellationToken);

        // 6. Update monitoring state atomically with trend result.
        var now = _timeProvider.GetUtcNow();
        var newMonitoringState = (monitoringState ?? CreateInitialMonitoringState(evt)) with
        {
            IsInitialized = true,
            IsActive = true,
            LastProcessedCandleTime = evt.CandleStartTime,
            LastEvaluationTime = now,
            LastReceivedDataTime = now,
            TrendDirection = trendResult.Direction,
            TrendCondition = trendResult.Condition,
            TrendEstablishedAt = trendResult.EstablishedAt,
            ConfirmedRetestCount = trendResult.BoundaryRetestCount,
            LastError = null,
            ConsecutiveErrors = 0
        };

        await _monitoringStateRepository.SaveAsync(newMonitoringState, cancellationToken);

        _logger.LogInformation(
            "Processed {Symbol} {Timeframe} candle at {Time}. Trend: {Direction}/{Condition}",
            symbol, timeframe, evt.CandleStartTime, trendResult.Direction, trendResult.Condition);

        // 7. Publish trend update event.
        var trendEvent = new TrendUpdatedEvent(
            Provider: provider,
            CanonicalSymbol: symbol,
            Timeframe: timeframe,
            Direction: trendResult.Direction,
            Condition: trendResult.Condition,
            PreviousDirection: previousDirection,
            PreviousCondition: previousCondition,
            TrendResult: trendResult,
            Timestamp: now);

        _trendUpdatedChannel.Writer.TryWrite(trendEvent);
    }

    private static MarketMonitoringState CreateInitialMonitoringState(ClosedCandleEvent evt) => new()
    {
        Provider = evt.Provider,
        CanonicalSymbol = evt.CanonicalSymbol,
        NativeSymbol = evt.NativeSymbol,
        Timeframe = evt.Timeframe,
        IsInitialized = false,
        IsActive = true
    };
}
