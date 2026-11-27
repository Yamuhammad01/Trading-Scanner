namespace TradingScanner.Domain.MarketScanning.Models;

public sealed record MarketUniverse
{
    public required string Provider { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int TotalDiscoveredContracts { get; init; }
    public int TotalEligibleContracts { get; init; }
    public IReadOnlyList<MarketUniverseEntry> Markets { get; init; } = [];

    public IReadOnlyList<MarketUniverseEntry> ActiveMarkets =>
        Markets.Where(m => m.IsActive).OrderBy(m => m.Rank ?? int.MaxValue).ToList();

    public static MarketUniverse Empty(string provider) => new()
    {
        Provider = provider,
        UpdatedAt = DateTimeOffset.MinValue,
        Markets = []
    };
}
