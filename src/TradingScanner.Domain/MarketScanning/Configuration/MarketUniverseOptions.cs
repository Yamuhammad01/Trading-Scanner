using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Domain.MarketScanning.Configuration;

public sealed class MarketUniverseOptions
{
    public bool DiscoveryEnabled { get; set; } = true;
    public int RefreshIntervalMinutes { get; set; } = 30;
    public MarketSelectionMode SelectionMode { get; set; } = MarketSelectionMode.TopByQuoteVolume;
    public int MaxActiveMarkets { get; set; } = 50;
    public decimal Minimum24HourQuoteVolume { get; set; } = 1_000_000m;
    public bool ExcludeInactiveContracts { get; set; } = true;
    public bool ExcludeExpiredContracts { get; set; } = true;
    public string RequiredSettlementCurrency { get; set; } = "USDT";
}
