using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Models;
using TradingScanner.Domain.Notifications;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.Configuration;
using TradingScanner.Infrastructure.GateIo;
using TradingScanner.Infrastructure.MarketScanning.Discovery;
using TradingScanner.Infrastructure.MarketScanning.Initialization;
using TradingScanner.Infrastructure.MarketScanning.Monitoring;
using TradingScanner.Infrastructure.MarketScanning.Notifications;
using TradingScanner.Infrastructure.MarketScanning.Persistence;
using TradingScanner.Infrastructure.MarketScanning.Processing;
using TradingScanner.Infrastructure.MarketScanning.Recovery;
using TradingScanner.Infrastructure.MarketScanning.Streaming;

namespace TradingScanner.Infrastructure.MarketScanning;

public static class MarketScanningServiceCollectionExtensions
{
    public static IServiceCollection AddMarketScanning(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind options.
        var universeOptions = new MarketUniverseOptions();
        configuration.GetSection("MarketUniverse").Bind(universeOptions);
        services.AddSingleton(universeOptions);

        var initOptions = new MarketInitializationOptions();
        configuration.GetSection("MarketInitialization").Bind(initOptions);
        services.AddSingleton(initOptions);

        // Symbol normalizer (singleton, shared between discovery and data provider).
        services.AddSingleton<ISymbolNormalizer, GateIoSymbolNormalizer>();

        // Market discovery.
        services.AddHttpClient<GateIoMarketDiscoveryProvider>((sp, client) =>
        {
            var providerOptions = sp.GetRequiredService<GateIoMarketDataProviderOptions>();
            client.BaseAddress = new Uri(providerOptions.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TradingScannerMVP/1.0");
        });

        services.AddSingleton<IMarketDiscoveryProvider>(sp =>
            sp.GetRequiredService<GateIoMarketDiscoveryProvider>());

        services.AddSingleton<IMarketEligibilityFilter, MarketEligibilityFilter>();
        services.AddSingleton<IMarketRankingService, MarketRankingService>();

        // Persistence.
        var universePath = Path.Combine(AppContext.BaseDirectory, "Data", "market-universe.json");
        services.AddSingleton<IMarketUniverseRepository>(_ =>
            new FileMarketUniverseRepository(universePath));

        var monitoringStatePath = Path.Combine(AppContext.BaseDirectory, "Data", "market-monitoring-state.json");
        services.AddSingleton<IMarketMonitoringStateRepository>(_ =>
            new FileMarketMonitoringStateRepository(monitoringStatePath));

        // Market universe manager.
        services.AddSingleton<IMarketUniverseManager, MarketUniverseManager>();

        // Candle buffer.
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<MarketInitializationOptions>();
            return new MarketCandleBuffer(opts.MaxRetainedCandlesPerMarket);
        });

        // Trend detector readiness (checks if rolling slope options are fully configured).
        services.AddSingleton(sp =>
        {
            var slopeOptions = sp.GetService<RollingSlopeOptions>();
            if (slopeOptions is null ||
                !slopeOptions.MinimumSlopeAtrPerCandle.HasValue ||
                slopeOptions.MinimumSlopeAtrPerCandle.Value <= 0m ||
                !slopeOptions.UpperBreakoutThresholdAtr.HasValue ||
                slopeOptions.UpperBreakoutThresholdAtr.Value <= 0m)
            {
                return TrendDetectorReadiness.NotReady(
                    "Rolling slope configuration is incomplete. " +
                    "MinimumSlopeAtrPerCandle and UpperBreakoutThresholdAtr must be configured and > 0.");
            }

            var detector = sp.GetService<ITrendDetector>();
            if (detector is null)
            {
                return TrendDetectorReadiness.NotReady("ITrendDetector is not registered.");
            }

            return TrendDetectorReadiness.Ready(detector);
        });

        // Market initialization service.
        services.AddSingleton<MarketInitializationService>();

        // Market monitoring options.
        var monitoringOptions = new MarketMonitoringOptions();
        configuration.GetSection("MarketMonitoring").Bind(monitoringOptions);
        services.AddSingleton(monitoringOptions);

        // Telegram notification options.
        var telegramNotifyOptions = new TelegramNotificationOptions();
        configuration.GetSection("Telegram").Bind(telegramNotifyOptions);
        services.AddSingleton(telegramNotifyOptions);

        // Streaming WebSocket data stream.
        services.AddSingleton<IMarketDataStream, GateIoFuturesWebSocketStream>();

        // Processing & Coordinator.
        services.AddSingleton<ClosedCandleProcessor>();
        services.AddSingleton<MarketProcessingCoordinator>();

        // REST reconciliation.
        services.AddSingleton<RestReconciliationService>();

        // Trend notifications.
        services.AddSingleton<TrendNotificationService>(sp =>
        {
            var teleOptions = sp.GetService<TelegramOptions>();
            var teleService = sp.GetService<TelegramNotificationService>();
            var dedupService = sp.GetService<AlertDeduplicationService>();
            var notifOptions = sp.GetRequiredService<TelegramNotificationOptions>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<TrendNotificationService>>();

            return new TrendNotificationService(notifOptions, teleService, dedupService, logger);
        });

        // Hosted background service.
        services.AddHostedService<Hosting.MarketScannerHostedService>();

        return services;
    }
}
