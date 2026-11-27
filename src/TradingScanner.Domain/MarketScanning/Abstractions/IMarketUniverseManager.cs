using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Domain.MarketScanning.Abstractions;

public interface IMarketUniverseManager
{
    MarketUniverse CurrentUniverse { get; }

    Task<MarketUniverse> LoadPersistedAsync(CancellationToken cancellationToken = default);

    Task<MarketUniverseDiff> RefreshAsync(CancellationToken cancellationToken = default);

    string? ResolveNativeSymbol(string canonicalSymbol);
}
