using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingScanner.Domain.MarketData;
using TradingScanner.Infrastructure.Bybit;
using TradingScanner.Infrastructure.GateIo;

namespace Microsoft.Extensions.DependencyInjection;

public static class MarketDataServiceCollectionExtensions
{
    private static readonly HashSet<string> SupportedProviders =
        new(StringComparer.OrdinalIgnoreCase) { "Bybit", "GateIo" };

    public static IServiceCollection AddBybitMarketDataProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var bybitSection = configuration.GetSection("Providers:Bybit");
        var options = bybitSection.Get<BybitMarketDataProviderOptions>()
            ?? new BybitMarketDataProviderOptions();

        services.AddHttpClient<BybitMarketDataProvider>((sp, client) =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(100);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TradingScannerMVP/1.0");
        });

        services.AddSingleton(options);
        services.AddTransient<IMarketDataProvider>(sp =>
            sp.GetRequiredService<BybitMarketDataProvider>());

        return services;
    }

    public static IServiceCollection AddGateIoMarketDataProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var gateIoSection = configuration.GetSection("Providers:GateIo");
        var options = gateIoSection.Get<GateIoMarketDataProviderOptions>()
            ?? new GateIoMarketDataProviderOptions();

        services.AddHttpClient<GateIoMarketDataProvider>((sp, client) =>
        {
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(100);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TradingScannerMVP/1.0");
        });

        services.AddSingleton(options);
        services.AddSingleton<GateIoContractMetadataCache>(
            sp => new GateIoContractMetadataCache(
                options.ContractMetadataCacheDurationMinutes));
        services.AddTransient<IMarketDataProvider>(sp =>
            sp.GetRequiredService<GateIoMarketDataProvider>());

        return services;
    }

    public static IServiceCollection AddMarketDataProviderFromConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var marketDataSection = configuration.GetSection("MarketData");
        var providerName = marketDataSection["Provider"];

        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new InvalidOperationException(
                "Market data provider is not configured. " +
                $"Set 'MarketData:Provider' to one of: {string.Join(", ", SupportedProviders)}.");
        }

        if (!SupportedProviders.Contains(providerName))
        {
            throw new InvalidOperationException(
                $"Unsupported market data provider '{providerName}'. " +
                $"Supported providers: {string.Join(", ", SupportedProviders)}.");
        }

        if (string.Equals(providerName, "Bybit", StringComparison.OrdinalIgnoreCase))
        {
            return services.AddBybitMarketDataProvider(configuration);
        }

        if (string.Equals(providerName, "GateIo", StringComparison.OrdinalIgnoreCase))
        {
            return services.AddGateIoMarketDataProvider(configuration);
        }

        // Unreachable due to the check above, but kept for clarity.
        throw new InvalidOperationException(
            $"Unsupported market data provider '{providerName}'. " +
            $"Supported providers: {string.Join(", ", SupportedProviders)}.");
    }
}

