namespace TradingScanner.Infrastructure.Bybit;

public sealed class BybitMarketDataProviderOptions
{
    public string BaseUrl { get; init; } = "https://api.bybit.com";

    public string Category { get; init; } = "linear";
}
