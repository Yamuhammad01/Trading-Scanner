namespace TradingScanner.Domain.MarketScanning.Configuration;

public sealed class MarketInitializationOptions
{
    public const int MinimumRequiredCandlesForRollingSlope = 98; // 84 lookback + 14 ATR warm-up

    public string Timeframe { get; set; } = "4H";
    public int HistoricalLookbackCandles { get; set; } = 120;
    public int MaxConcurrentInitializations { get; set; } = 5;
    public int MaxRetainedCandlesPerMarket { get; set; } = 250;
}
