namespace TradingScanner.Domain.MarketScanning.Abstractions;

public interface ISymbolNormalizer
{
    string ProviderName { get; }

    string ToCanonicalSymbol(string nativeSymbol);

    string ToNativeSymbol(string canonicalSymbol);

    void RegisterMapping(string canonicalSymbol, string nativeSymbol);
}
