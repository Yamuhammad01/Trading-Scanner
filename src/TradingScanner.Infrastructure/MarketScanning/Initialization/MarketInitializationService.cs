using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Models;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.Configuration;
using TradingScanner.Infrastructure.MarketScanning.Monitoring;
using TradingScanner.Infrastructure.Services;

namespace TradingScanner.Infrastructure.MarketScanning.Initialization;

public sealed class MarketInitializationService
{
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly HistoricalCandleLoader _historicalLoader;
    private readonly MarketCandleBuffer _candleBuffer;
    private readonly IMarketMonitoringStateRepository _monitoringStateRepository;
    private readonly ITrendStateRepository _trendStateRepository;
    private readonly TrendDetectorReadiness _trendReadiness;
    private readonly RollingSlopeOptions? _rollingSlopeOptions;
    private readonly MarketInitializationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarketInitializationService> _logger;

    public MarketInitializationService(
        IMarketDataProvider marketDataProvider,
        HistoricalCandleLoader historicalLoader,
        MarketCandleBuffer candleBuffer,
        IMarketMonitoringStateRepository monitoringStateRepository,
        ITrendStateRepository trendStateRepository,
        TrendDetectorReadiness trendReadiness,
        MarketInitializationOptions options,
        RollingSlopeOptions? rollingSlopeOptions = null,
        TimeProvider? timeProvider = null,
        ILogger<MarketInitializationService>? logger = null)
    {
        _marketDataProvider = marketDataProvider ?? throw new ArgumentNullException(nameof(marketDataProvider));
        _historicalLoader = historicalLoader ?? throw new ArgumentNullException(nameof(historicalLoader));
        _candleBuffer = candleBuffer ?? throw new ArgumentNullException(nameof(candleBuffer));
        _monitoringStateRepository = monitoringStateRepository ?? throw new ArgumentNullException(nameof(monitoringStateRepository));
        _trendStateRepository = trendStateRepository ?? throw new ArgumentNullException(nameof(trendStateRepository));
        _trendReadiness = trendReadiness ?? throw new ArgumentNullException(nameof(trendReadiness));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rollingSlopeOptions = rollingSlopeOptions;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<MarketInitializationService>.Instance;
    }

    public async Task<IReadOnlyList<MarketInitializationResult>> InitializeMarketsAsync(
        IReadOnlyList<MarketUniverseEntry> markets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markets);
        if (markets.Count == 0)
        {
            return [];
        }

        var maxConcurrency = Math.Max(1, _options.MaxConcurrentInitializations);
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = markets.Select(async market =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await InitializeSingleMarketAsync(market, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        return await Task.WhenAll(tasks);
    }

    public async Task<MarketInitializationResult> InitializeSingleMarketAsync(
        MarketUniverseEntry market,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(market);

        var providerName = _marketDataProvider.ProviderName;
        var timeframe = Timeframe.Parse(_options.Timeframe);
        var tfName = timeframe.Name.ToUpperInvariant();
        var now = _timeProvider.GetUtcNow();

        var existingMonitoring = await _monitoringStateRepository.GetAsync(
            providerName,
            market.CanonicalSymbol,
            tfName,
            cancellationToken);

        try
        {
            var lookback = Math.Max(
                _options.HistoricalLookbackCandles,
                MarketInitializationOptions.MinimumRequiredCandlesForRollingSlope);

            var latestClosed = GetLatestClosedCandleStart(now, timeframe.Interval);
            var oldestRequired = latestClosed - TimeSpan.FromTicks(timeframe.Interval.Ticks * (lookback - 1));

            var history = await _historicalLoader.LoadRangeAsync(
                market.CanonicalSymbol,
                timeframe,
                oldestRequired,
                latestClosed,
                cancellationToken);

            _candleBuffer.Initialize(providerName, market.CanonicalSymbol, tfName, history.Candles);

            var configId = _rollingSlopeOptions?.ConfigurationId ?? "rolling_slope_trend_detector";
            var existingTrendState = await _trendStateRepository.GetAsync(
                providerName,
                market.CanonicalSymbol,
                tfName,
                configId,
                cancellationToken);

            var lastProcessedTime = existingTrendState?.LastProcessedCandleTime ?? existingMonitoring?.LastProcessedCandleTime;
            var direction = existingTrendState?.Direction ?? existingMonitoring?.TrendDirection ?? Domain.TrendDetection.RollingSlope.Models.TrendDirection.Neutral;
            var condition = existingTrendState?.Condition ?? existingMonitoring?.TrendCondition ?? Domain.TrendDetection.RollingSlope.Models.TrendCondition.NoValidTrend;
            var establishedAt = existingTrendState?.EstablishedAt ?? existingMonitoring?.TrendEstablishedAt;
            var retestCount = existingTrendState?.BoundaryRetestCount ?? existingMonitoring?.ConfirmedRetestCount ?? 0;

            if (_trendReadiness.IsReady && _trendReadiness.Detector is not null)
            {
                // Replay only catches up candles newer than persisted LastProcessedCandleTime because
                // RollingSlopeTrendDetector skips candles <= LastProcessedCandleTime.
                if (!lastProcessedTime.HasValue || lastProcessedTime.Value < history.LatestCandleTime)
                {
                    var replayResults = await _trendReadiness.Detector.ReplayAsync(
                        providerName,
                        market.CanonicalSymbol,
                        tfName,
                        history.Candles,
                        cancellationToken);

                    if (replayResults.Count > 0)
                    {
                        var latest = replayResults[^1];
                        lastProcessedTime = latest.LastProcessedCandleTime;
                        direction = latest.Direction;
                        condition = latest.Condition;
                        establishedAt = latest.EstablishedAt;
                        retestCount = latest.BoundaryRetestCount;
                    }
                }
            }

            var updatedMonitoring = new MarketMonitoringState
            {
                Provider = providerName,
                CanonicalSymbol = market.CanonicalSymbol,
                NativeSymbol = market.NativeSymbol,
                Timeframe = tfName,
                IsInitialized = true,
                IsActive = true,
                LastProcessedCandleTime = lastProcessedTime,
                LastEvaluationTime = now,
                LastReceivedDataTime = existingMonitoring?.LastReceivedDataTime,
                TrendDirection = direction,
                TrendCondition = condition,
                TrendEstablishedAt = establishedAt,
                ConfirmedRetestCount = retestCount,
                LastError = null,
                ConsecutiveErrors = 0
            };

            await _monitoringStateRepository.SaveAsync(updatedMonitoring, cancellationToken);

            _logger.LogInformation(
                "Initialized market {Symbol} ({NativeSymbol}) with {Count} {Timeframe} candles. Latest={Latest:O}, Trend={Direction}/{Condition}",
                market.CanonicalSymbol,
                market.NativeSymbol,
                history.Candles.Count,
                tfName,
                history.LatestCandleTime,
                direction,
                condition);

            return new MarketInitializationResult(
                providerName,
                market.CanonicalSymbol,
                market.NativeSymbol,
                Succeeded: true,
                LoadedCandles: history.Candles.Count,
                LatestClosedCandleTime: history.LatestCandleTime);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to initialize market {Symbol} ({NativeSymbol}) for {Timeframe}.",
                market.CanonicalSymbol,
                market.NativeSymbol,
                tfName);

            var failedState = (existingMonitoring ?? new MarketMonitoringState
            {
                Provider = providerName,
                CanonicalSymbol = market.CanonicalSymbol,
                NativeSymbol = market.NativeSymbol,
                Timeframe = tfName
            }) with
            {
                IsInitialized = false,
                IsActive = true,
                LastError = ex.Message,
                ConsecutiveErrors = (existingMonitoring?.ConsecutiveErrors ?? 0) + 1
            };

            await _monitoringStateRepository.SaveAsync(failedState, cancellationToken);

            return new MarketInitializationResult(
                providerName,
                market.CanonicalSymbol,
                market.NativeSymbol,
                Succeeded: false,
                LoadedCandles: 0,
                LatestClosedCandleTime: null,
                ErrorMessage: ex.Message);
        }
    }

    private static DateTimeOffset GetLatestClosedCandleStart(DateTimeOffset now, TimeSpan interval)
    {
        var intervalMs = (long)interval.TotalMilliseconds;
        var alignedMs = (now.ToUnixTimeMilliseconds() / intervalMs) * intervalMs;
        return DateTimeOffset.FromUnixTimeMilliseconds(alignedMs) - interval;
    }
}
