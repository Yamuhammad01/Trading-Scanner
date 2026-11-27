namespace TradingScanner.Domain.MarketScanning.Models;

public sealed record MarketUniverseEntry
{
    public required string Provider { get; init; }
    public required string CanonicalSymbol { get; init; }
    public required string NativeSymbol { get; init; }
    public required string SettlementCurrency { get; init; }
    public string ContractStatus { get; init; } = string.Empty;
    public bool IsEligible { get; init; }
    public bool IsActive { get; init; }
    public int? Rank { get; init; }
    public decimal QuoteVolume24h { get; init; }
    public decimal LastPrice { get; init; }
    public decimal QuantoMultiplier { get; init; }
    public DateTimeOffset DiscoveredAt { get; init; }
    public DateTimeOffset LastTickerUpdate { get; init; }
    public DateTimeOffset? DeactivatedAt { get; init; }
    public string? IneligibilityReason { get; init; }
}
