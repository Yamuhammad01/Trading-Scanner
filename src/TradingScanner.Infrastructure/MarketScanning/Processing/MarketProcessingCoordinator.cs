using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Events;

namespace TradingScanner.Infrastructure.MarketScanning.Processing;

public sealed class MarketProcessingCoordinator : IAsyncDisposable
{
    private readonly ClosedCandleProcessor _processor;
    private readonly MarketMonitoringOptions _options;
    private readonly ILogger<MarketProcessingCoordinator> _logger;
    private readonly ConcurrentDictionary<string, Channel<ClosedCandleEvent>> _perMarketChannels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _perMarketTasks = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;

    public MarketProcessingCoordinator(
        ClosedCandleProcessor processor,
        MarketMonitoringOptions options,
        ILogger<MarketProcessingCoordinator>? logger = null)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<MarketProcessingCoordinator>.Instance;
    }

    public void Start(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public async Task EnqueueAsync(ClosedCandleEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var key = $"{evt.Provider}|{evt.CanonicalSymbol}|{evt.Timeframe}";
        var channel = _perMarketChannels.GetOrAdd(key, _ =>
        {
            var ch = Channel.CreateBounded<ClosedCandleEvent>(
                new BoundedChannelOptions(50)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleWriter = false,
                    SingleReader = true
                });

            // Spin up a dedicated consumer for this market.
            var linkedCt = _cts?.Token ?? cancellationToken;
            var task = Task.Run(() => ProcessMarketAsync(key, ch.Reader, linkedCt), linkedCt);
            _perMarketTasks.TryAdd(key, task);

            return ch;
        });

        await channel.Writer.WriteAsync(evt, cancellationToken);
    }

    private async Task ProcessMarketAsync(
        string key,
        ChannelReader<ClosedCandleEvent> reader,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Market processor started for {Key}.", key);

        await foreach (var evt in reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await _processor.ProcessAsync(evt, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error processing candle for {Symbol} {Timeframe} at {Time}. Event will not be retried automatically.",
                    evt.CanonicalSymbol, evt.Timeframe, evt.CandleStartTime);
            }
        }

        _logger.LogDebug("Market processor stopped for {Key}.", key);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        foreach (var channel in _perMarketChannels.Values)
        {
            channel.Writer.TryComplete();
        }

        foreach (var task in _perMarketTasks.Values)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _perMarketChannels.Clear();
        _perMarketTasks.Clear();
        _cts?.Dispose();
    }
}
