using TradingScanner.Domain.MarketData;

namespace TradingScanner.Domain.MarketScanning.Models;

public sealed record MarketInstrument
{
    public required string Provider { get; init; }
    public required string CanonicalSymbol { get; init; }
    public required string NativeSymbol { get; init; }
    public MarketType MarketType { get; init; } = MarketType.USDTPerpetual;
    public required string SettlementCurrency { get; init; }
    public bool IsPerpetual { get; init; }
    public bool IsActive { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool InDelisting { get; init; }
    public DateTimeOffset? ExpiryTime { get; init; }
    public decimal QuantoMultiplier { get; init; }
    public DateTimeOffset DiscoveredAt { get; init; }
}
