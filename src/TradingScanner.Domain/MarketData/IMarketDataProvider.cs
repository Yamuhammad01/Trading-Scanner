using TradingScanner.Domain.Entities;

namespace TradingScanner.Domain.MarketData;

public interface IMarketDataProvider
{
    string ProviderName { get; }

    Task<IReadOnlyList<Candle>> GetCandlesAsync(
        MarketDataRequest request,
        CancellationToken cancellationToken = default);
}
