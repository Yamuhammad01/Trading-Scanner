namespace TradingScanner.Api.Dtos;

public sealed record MarketOverviewDto
{
    public int TotalDiscoveredMarkets { get; init; }
    public int TotalInitializedMarkets { get; init; }
    public int TotalActivelyMonitoredMarkets { get; init; }
    public int BullishMarketCount { get; init; }
    public int BearishMarketCount { get; init; }
    public int NeutralMarketCount { get; init; }
    public int DevelopingTrendCount { get; init; }
    public int AcceleratingTrendCount { get; init; }
    public int MatureTrendCount { get; init; }
    public int InitializingMarketCount { get; init; }
    public int FailedOrUnavailableMarketCount { get; init; }
    public string CurrentTimeframe { get; init; } = string.Empty;
    public string ActiveExchangeProvider { get; init; } = string.Empty;
    public string ScannerStatus { get; init; } = string.Empty;
    public DateTimeOffset? LastSuccessfulMarketDataUpdate { get; init; }
    public TimeSpan? ScannerUptime { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}