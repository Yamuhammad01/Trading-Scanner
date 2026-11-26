using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.Configuration;
using TradingScanner.Domain.TrendDetection.RollingSlope;

namespace TradingScanner.Infrastructure.TrendDetection;

public static class TrendDetectionServiceCollectionExtensions
{
    public static IServiceCollection AddRollingSlopeTrendDetection(
        this IServiceCollection services,
        IConfiguration configuration,
        string? configFilePath = null,
        string? stateFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var resolvedConfigPath = configFilePath
            ?? Path.Combine(AppContext.BaseDirectory, "Configuration", "rolling-slope-4h.json");

        var resolvedStatePath = stateFilePath
            ?? Path.Combine(AppContext.BaseDirectory, "Data", "rolling-slope-trend-state.json");

        var options = RollingSlopeConfigurationLoader.LoadAndValidate(
            resolvedConfigPath,
            configuration);

        services.AddSingleton(options);
        services.AddSingleton<RegressionCalculator>();
        services.AddSingleton<AtrCalculator>();
        services.AddSingleton<ChannelBoundaryCalculator>();
        services.AddSingleton<BoundaryInteractionEvaluator>();
        services.AddSingleton<TrendMaturityEvaluator>();
        services.AddSingleton<TrendStateManager>();
        services.AddSingleton<ITrendStateRepository>(_ => new FileTrendStateRepository(resolvedStatePath));
        services.AddSingleton<ITrendDetectionLogger, LoggerTrendDetectionLogger>();
        services.AddSingleton<ITrendDetector, RollingSlopeTrendDetector>();

        return services;
    }
}
