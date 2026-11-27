namespace TradingScanner.Domain.MarketScanning.Models;

public sealed record MarketUniverseDiff
{
    public IReadOnlyList<MarketUniverseEntry> Added { get; init; } = [];
    public IReadOnlyList<MarketUniverseEntry> Removed { get; init; } = [];
    public IReadOnlyList<MarketUniverseEntry> MetadataUpdated { get; init; } = [];
    public IReadOnlyList<MarketUniverseEntry> Unchanged { get; init; } = [];

    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || MetadataUpdated.Count > 0;
}
