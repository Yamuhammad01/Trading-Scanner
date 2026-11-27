using System.Collections.Concurrent;
using TradingScanner.Domain.Entities;

namespace TradingScanner.Infrastructure.MarketScanning.Monitoring;

public sealed class MarketCandleBuffer
{
    private readonly ConcurrentDictionary<string, SymbolBuffer> _buffers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly int _maxCandles;

    public MarketCandleBuffer(int maxCandles = 250)
    {
        _maxCandles = Math.Max(1, maxCandles);
    }

    public void Initialize(string provider, string symbol, string timeframe, IEnumerable<Candle> candles)
    {
        var key = BuildKey(provider, symbol, timeframe);
        var buffer = _buffers.GetOrAdd(key, _ => new SymbolBuffer(_maxCandles));
        buffer.ReplaceAll(candles);
    }

    public bool TryAppend(string provider, string symbol, string timeframe, Candle candle)
    {
        var key = BuildKey(provider, symbol, timeframe);
        var buffer = _buffers.GetOrAdd(key, _ => new SymbolBuffer(_maxCandles));
        return buffer.TryAppend(candle);
    }

    public IReadOnlyList<Candle> GetSnapshot(string provider, string symbol, string timeframe)
    {
        var key = BuildKey(provider, symbol, timeframe);
        return _buffers.TryGetValue(key, out var buffer) ? buffer.GetSnapshot() : [];
    }

    public DateTimeOffset? GetLatestCandleTime(string provider, string symbol, string timeframe)
    {
        var key = BuildKey(provider, symbol, timeframe);
        return _buffers.TryGetValue(key, out var buffer) ? buffer.GetLatestCandleTime() : null;
    }

    private static string BuildKey(string provider, string symbol, string timeframe) =>
        $"{provider.Trim()}|{symbol.Trim().ToUpperInvariant()}|{timeframe.Trim().ToUpperInvariant()}";

    private sealed class SymbolBuffer
    {
        private readonly object _sync = new();
        private readonly int _maxCandles;
        private readonly SortedList<DateTimeOffset, Candle> _candles = new();

        public SymbolBuffer(int maxCandles)
        {
            _maxCandles = maxCandles;
        }

        public void ReplaceAll(IEnumerable<Candle> candles)
        {
            lock (_sync)
            {
                _candles.Clear();
                foreach (var c in candles)
                {
                    _candles[c.StartTime] = c;
                }

                TrimExcess();
            }
        }

        public bool TryAppend(Candle candle)
        {
            lock (_sync)
            {
                if (_candles.TryGetValue(candle.StartTime, out var existing))
                {
                    if (existing != candle)
                    {
                        throw new InvalidOperationException(
                            $"Conflicting duplicate candle for {candle.Symbol} at {candle.StartTime:O}.");
                    }

                    return false;
                }

                _candles[candle.StartTime] = candle;
                TrimExcess();
                return true;
            }
        }

        public IReadOnlyList<Candle> GetSnapshot()
        {
            lock (_sync)
            {
                return _candles.Values.ToList();
            }
        }

        public DateTimeOffset? GetLatestCandleTime()
        {
            lock (_sync)
            {
                return _candles.Count > 0 ? _candles.Keys[^1] : null;
            }
        }

        private void TrimExcess()
        {
            while (_candles.Count > _maxCandles)
            {
                _candles.RemoveAt(0);
            }
        }
    }
}
