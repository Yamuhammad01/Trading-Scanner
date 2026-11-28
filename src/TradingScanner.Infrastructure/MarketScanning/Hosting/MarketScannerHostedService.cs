using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Events;
using TradingScanner.Domain.MarketScanning.Models;
using TradingScanner.Infrastructure.MarketScanning.Initialization;
using TradingScanner.Infrastructure.MarketScanning.Notifications;
using TradingScanner.Infrastructure.MarketScanning.Processing;
using TradingScanner.Infrastructure.MarketScanning.Recovery;

namespace TradingScanner.Infrastructure.MarketScanning.Hosting;

public sealed class MarketScannerHostedService : BackgroundService
{
    private readonly IMarketUniverseManager _universeManager;
    private readonly MarketInitializationService _initializationService;
    private readonly IMarketDataStream? _dataStream;
    private readonly MarketProcessingCoordinator _coordinator;
    private readonly RestReconciliationService _reconciliationService;
    private readonly TrendNotificationService _notificationService;
    private readonly ClosedCandleProcessor _processor;
    private readonly MarketUniverseOptions _universeOptions;
    private readonly MarketMonitoringOptions _monitoringOptions;
    private readonly MarketInitializationOptions _initOptions;
    private readonly ILogger<MarketScannerHostedService> _logger;

    public MarketScannerHostedService(
        IMarketUniverseManager universeManager,
        MarketInitializationService initializationService,
        MarketProcessingCoordinator coordinator,
        RestReconciliationService reconciliationService,
        TrendNotificationService notificationService,
        ClosedCandleProcessor processor,
        MarketUniverseOptions universeOptions,
        MarketMonitoringOptions monitoringOptions,
        MarketInitializationOptions initOptions,
        ILogger<MarketScannerHostedService> logger,
        IMarketDataStream? dataStream = null)
    {
        _universeManager = universeManager;
        _initializationService = initializationService;
        _coordinator = coordinator;
        _reconciliationService = reconciliationService;
        _notificationService = notificationService;
        _processor = processor;
        _universeOptions = universeOptions;
        _monitoringOptions = monitoringOptions;
        _initOptions = initOptions;
        _logger = logger;
        _dataStream = dataStream;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Market Scanner starting...");

        // 1. Load persisted universe.
        await _universeManager.LoadPersistedAsync(stoppingToken);
        _logger.LogInformation(
            "Loaded persisted universe: {Count} markets.",
            _universeManager.CurrentUniverse.Markets.Count);

        // 2. Fresh market discovery.
        MarketUniverseDiff diff;
        try
        {
            diff = await _universeManager.RefreshAsync(stoppingToken);
            LogUniverseDiff(diff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial market discovery failed. Continuing with persisted universe of {Count} markets.",
                _universeManager.CurrentUniverse.Markets.Count);
            diff = new MarketUniverseDiff();
        }

        // 3. Initialize all active markets (historical candle loading + trend replay).
        var activeMarkets = _universeManager.CurrentUniverse.ActiveMarkets;
        if (activeMarkets.Count > 0)
        {
            _logger.LogInformation("Initializing {Count} active markets...", activeMarkets.Count);
            var initResults = await _initializationService.InitializeMarketsAsync(activeMarkets, stoppingToken);
            var succeeded = initResults.Count(r => r.Succeeded);
            var failed = initResults.Count(r => !r.Succeeded);
            _logger.LogInformation(
                "Market initialization complete: {Succeeded} succeeded, {Failed} failed.",
                succeeded, failed);
        }

        // 4. Start the processing coordinator.
        _coordinator.Start(stoppingToken);

        // 5. REST reconciliation for missed candles.
        if (_monitoringOptions.RestReconciliationEnabled)
        {
            await ReconcileAllMarketsAsync(activeMarkets, stoppingToken);
        }

        // 6. Start concurrent tasks.
        var tasks = new List<Task>();

        // WebSocket monitoring.
        if (_monitoringOptions.WebSocketEnabled && _dataStream is not null)
        {
            tasks.Add(Task.Run(() => RunWebSocketMonitoringAsync(stoppingToken), stoppingToken));
        }

        // Periodic market discovery refresh.
        if (_universeOptions.DiscoveryEnabled)
        {
            tasks.Add(Task.Run(() => RunPeriodicDiscoveryAsync(stoppingToken), stoppingToken));
        }

        // Periodic REST reconciliation.
        if (_monitoringOptions.RestReconciliationEnabled)
        {
            tasks.Add(Task.Run(() => RunPeriodicReconciliationAsync(stoppingToken), stoppingToken));
        }

        // Trend notification consumer.
        tasks.Add(Task.Run(() => RunTrendNotificationConsumerAsync(stoppingToken), stoppingToken));

        _logger.LogInformation(
            "Market Scanner operational. Monitoring {Count} markets on {Timeframes} timeframes.",
            activeMarkets.Count,
            string.Join(", ", _monitoringOptions.Timeframes));

        await Task.WhenAll(tasks);
        _logger.LogInformation("Market Scanner stopped.");
    }

    private async Task RunWebSocketMonitoringAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _dataStream!.ConnectAsync(cancellationToken);

                // Subscribe all active markets.
                var activeMarkets = _universeManager.CurrentUniverse.ActiveMarkets;
                var nativeSymbols = activeMarkets.Select(m => m.NativeSymbol).ToList();

                foreach (var tf in _monitoringOptions.Timeframes)
                {
                    var interval = Timeframe.Parse(tf).Name.ToLowerInvariant();
                    await _dataStream.SubscribeAsync(nativeSymbols, interval, cancellationToken);
                }

                // Process incoming closed candles.
                await foreach (var evt in _dataStream.ReadClosedCandlesAsync(cancellationToken))
                {
                    await _coordinator.EnqueueAsync(evt, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket monitoring error. Will reconnect.");
                await Task.Delay(5000, cancellationToken);
            }
        }
    }

    private async Task RunPeriodicDiscoveryAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMinutes(_universeOptions.RefreshIntervalMinutes);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);

                _logger.LogInformation("Running periodic market discovery refresh...");
                var discoveryDiff = await _universeManager.RefreshAsync(cancellationToken);
                LogUniverseDiff(discoveryDiff);

                // Initialize newly added markets.
                if (discoveryDiff.Added.Count > 0)
                {
                    _logger.LogInformation("Initializing {Count} newly discovered markets.", discoveryDiff.Added.Count);
                    var newEntries = discoveryDiff.Added
                        .Where(m => m.IsActive)
                        .ToList();

                    await _initializationService.InitializeMarketsAsync(newEntries, cancellationToken);

                    // Subscribe new markets to WebSocket.
                    if (_dataStream is not null && _dataStream.IsConnected)
                    {
                        var newNativeSymbols = newEntries.Select(m => m.NativeSymbol).ToList();
                        foreach (var tf in _monitoringOptions.Timeframes)
                        {
                            var tfInterval = Timeframe.Parse(tf).Name.ToLowerInvariant();
                            await _dataStream.SubscribeAsync(newNativeSymbols, tfInterval, cancellationToken);
                        }
                    }
                }

                // Unsubscribe removed markets from WebSocket.
                if (discoveryDiff.Removed.Count > 0 && _dataStream is not null && _dataStream.IsConnected)
                {
                    var removedNativeSymbols = discoveryDiff.Removed.Select(m => m.NativeSymbol).ToList();
                    foreach (var tf in _monitoringOptions.Timeframes)
                    {
                        var tfInterval = Timeframe.Parse(tf).Name.ToLowerInvariant();
                        await _dataStream.UnsubscribeAsync(removedNativeSymbols, tfInterval, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic market discovery refresh failed.");
            }
        }
    }

    private async Task RunPeriodicReconciliationAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMinutes(_monitoringOptions.RestReconciliationIntervalMinutes);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);
                var markets = _universeManager.CurrentUniverse.ActiveMarkets;
                await ReconcileAllMarketsAsync(markets, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic REST reconciliation failed.");
            }
        }
    }

    private async Task ReconcileAllMarketsAsync(
        IReadOnlyList<MarketUniverseEntry> markets,
        CancellationToken cancellationToken)
    {
        foreach (var market in markets)
        {
            try
            {
                var events = await _reconciliationService.ReconcileAsync(market, cancellationToken);
                foreach (var evt in events)
                {
                    await _coordinator.EnqueueAsync(evt, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation failed for {Symbol}. Continuing with other markets.",
                    market.CanonicalSymbol);
            }
        }
    }

    private async Task RunTrendNotificationConsumerAsync(CancellationToken cancellationToken)
    {
        await foreach (var evt in _processor.TrendUpdates.ReadAllAsync(cancellationToken))
        {
            try
            {
                await _notificationService.ProcessTrendUpdateAsync(evt, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process trend notification for {Symbol}.", evt.CanonicalSymbol);
            }
        }
    }

    private void LogUniverseDiff(MarketUniverseDiff diff)
    {
        if (!diff.HasChanges)
        {
            _logger.LogInformation("Market universe unchanged.");
            return;
        }

        _logger.LogInformation(
            "Market universe updated: +{Added} added, -{Removed} removed, ~{Updated} metadata changed.",
            diff.Added.Count, diff.Removed.Count, diff.MetadataUpdated.Count);

        foreach (var added in diff.Added)
        {
            _logger.LogInformation("  + {Symbol} (rank {Rank}, volume {Volume:N0})",
                added.CanonicalSymbol, added.Rank, added.QuoteVolume24h);
        }

        foreach (var removed in diff.Removed)
        {
            _logger.LogInformation("  - {Symbol}", removed.CanonicalSymbol);
        }
    }
}

