namespace TradingScanner.Domain.MarketScanning.Models;

public sealed record MarketTicker
{
    public required string Provider { get; init; }
    public required string NativeSymbol { get; init; }
    public required string CanonicalSymbol { get; init; }
    public decimal LastPrice { get; init; }
    public decimal MarkPrice { get; init; }
    public decimal QuoteVolume24h { get; init; }
    public decimal BaseVolume24h { get; init; }
    public decimal ContractVolume24h { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
