using TradingScanner.Domain.MarketScanning.Events;

namespace TradingScanner.Domain.MarketScanning.Abstractions;

public interface IMarketDataStream : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task SubscribeAsync(
        IReadOnlyList<string> nativeSymbols,
        string interval,
        CancellationToken cancellationToken = default);

    Task UnsubscribeAsync(
        IReadOnlyList<string> nativeSymbols,
        string interval,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ClosedCandleEvent> ReadClosedCandlesAsync(
        CancellationToken cancellationToken = default);
}
