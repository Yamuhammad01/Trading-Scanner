using System.Collections.Concurrent;
using TradingScanner.Domain.MarketScanning.Abstractions;

namespace TradingScanner.Infrastructure.GateIo;

public sealed class GateIoSymbolNormalizer : ISymbolNormalizer
{
    private readonly ConcurrentDictionary<string, string> _nativeToCanonical =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _canonicalToNative =
        new(StringComparer.OrdinalIgnoreCase);

    public string ProviderName => "Gate.io USDT Perpetual Futures";

    public void RegisterMapping(string canonicalSymbol, string nativeSymbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSymbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSymbol);

        var canonical = canonicalSymbol.Trim().ToUpperInvariant();
        var native = nativeSymbol.Trim().ToUpperInvariant();

        _nativeToCanonical[native] = canonical;
        _canonicalToNative[canonical] = native;
    }

    public string ToCanonicalSymbol(string nativeSymbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeSymbol);
        var upper = nativeSymbol.Trim().ToUpperInvariant();

        if (_nativeToCanonical.TryGetValue(upper, out var mapped))
        {
            return mapped;
        }

        var canonical = upper.Replace("_", string.Empty, StringComparison.Ordinal);
        RegisterMapping(canonical, upper);
        return canonical;
    }

    public string ToNativeSymbol(string canonicalSymbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalSymbol);
        var upper = canonicalSymbol.Trim().ToUpperInvariant();

        if (_canonicalToNative.TryGetValue(upper, out var mapped))
        {
            return mapped;
        }

        if (upper.Contains('_', StringComparison.Ordinal))
        {
            return upper;
        }

        if (upper.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) && upper.Length > 4)
        {
            var baseAsset = upper[..^4];
            return $"{baseAsset}_USDT";
        }

        return upper;
    }
}
