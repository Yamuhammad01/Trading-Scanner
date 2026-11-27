using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Infrastructure.MarketScanning.Discovery;

public sealed class MarketUniverseManager : IMarketUniverseManager
{
    private readonly IMarketDiscoveryProvider _discoveryProvider;
    private readonly IMarketEligibilityFilter _eligibilityFilter;
    private readonly IMarketRankingService _rankingService;
    private readonly IMarketUniverseRepository _repository;
    private readonly ISymbolNormalizer _symbolNormalizer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarketUniverseManager> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private MarketUniverse _currentUniverse;

    public MarketUniverseManager(
        IMarketDiscoveryProvider discoveryProvider,
        IMarketEligibilityFilter eligibilityFilter,
        IMarketRankingService rankingService,
        IMarketUniverseRepository repository,
        ISymbolNormalizer symbolNormalizer,
        TimeProvider? timeProvider = null,
        ILogger<MarketUniverseManager>? logger = null)
    {
        _discoveryProvider = discoveryProvider ?? throw new ArgumentNullException(nameof(discoveryProvider));
        _eligibilityFilter = eligibilityFilter ?? throw new ArgumentNullException(nameof(eligibilityFilter));
        _rankingService = rankingService ?? throw new ArgumentNullException(nameof(rankingService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _symbolNormalizer = symbolNormalizer ?? throw new ArgumentNullException(nameof(symbolNormalizer));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<MarketUniverseManager>.Instance;
        _currentUniverse = MarketUniverse.Empty(_discoveryProvider.ProviderName);
    }

    public MarketUniverse CurrentUniverse => _currentUniverse;

    public async Task<MarketUniverse> LoadPersistedAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var persisted = await _repository.GetAsync(_discoveryProvider.ProviderName, cancellationToken);
            if (persisted is null)
            {
                return _currentUniverse;
            }

            foreach (var entry in persisted.Markets)
            {
                _symbolNormalizer.RegisterMapping(entry.CanonicalSymbol, entry.NativeSymbol);
            }

            _currentUniverse = persisted;
            return persisted;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<MarketUniverseDiff> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var now = _timeProvider.GetUtcNow();
            var instruments = await _discoveryProvider.GetAvailableMarketsAsync(cancellationToken);
            var tickers = await _discoveryProvider.GetMarketTickersAsync(cancellationToken);

            var tickerByNative = tickers
                .GroupBy(t => t.NativeSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var eligible = new List<(MarketInstrument Instrument, MarketTicker Ticker)>();
            foreach (var instrument in instruments)
            {
                tickerByNative.TryGetValue(instrument.NativeSymbol, out var ticker);
                if (_eligibilityFilter.IsEligible(instrument, ticker, out _))
                {
                    eligible.Add((instrument, ticker!));
                }
            }

            var selectedActive = _rankingService.RankAndSelect(eligible, now);
            var selectedBySymbol = selectedActive.ToDictionary(
                m => m.CanonicalSymbol,
                StringComparer.OrdinalIgnoreCase);

            var previousBySymbol = _currentUniverse.Markets.ToDictionary(
                m => m.CanonicalSymbol,
                StringComparer.OrdinalIgnoreCase);

            var added = new List<MarketUniverseEntry>();
            var removed = new List<MarketUniverseEntry>();
            var metadataUpdated = new List<MarketUniverseEntry>();
            var unchanged = new List<MarketUniverseEntry>();
            var mergedMarkets = new List<MarketUniverseEntry>(selectedActive.Count + previousBySymbol.Count);

            foreach (var entry in selectedActive)
            {
                _symbolNormalizer.RegisterMapping(entry.CanonicalSymbol, entry.NativeSymbol);
                mergedMarkets.Add(entry);

                if (!previousBySymbol.TryGetValue(entry.CanonicalSymbol, out var prev) || !prev.IsActive)
                {
                    added.Add(entry);
                }
                else if (HasMetadataChanged(prev, entry))
                {
                    metadataUpdated.Add(entry);
                }
                else
                {
                    unchanged.Add(entry);
                }
            }

            // Preserve historical/inactive entries instead of deleting them.
            foreach (var prev in previousBySymbol.Values)
            {
                if (selectedBySymbol.ContainsKey(prev.CanonicalSymbol))
                {
                    continue;
                }

                var inactiveEntry = prev with
                {
                    IsActive = false,
                    IsEligible = false,
                    Rank = null,
                    DeactivatedAt = prev.DeactivatedAt ?? now,
                    IneligibilityReason = "No longer in top eligible active universe."
                };

                mergedMarkets.Add(inactiveEntry);
                if (prev.IsActive)
                {
                    removed.Add(inactiveEntry);
                }
            }

            var updatedUniverse = new MarketUniverse
            {
                Provider = _discoveryProvider.ProviderName,
                UpdatedAt = now,
                TotalDiscoveredContracts = instruments.Count,
                TotalEligibleContracts = eligible.Count,
                Markets = mergedMarkets
            };

            await _repository.SaveAsync(updatedUniverse, cancellationToken);
            _currentUniverse = updatedUniverse;

            _logger.LogInformation(
                "Market universe refreshed for {Provider}: discovered={Discovered}, eligible={Eligible}, active={Active}, added={Added}, removed={Removed}, updated={Updated}",
                _discoveryProvider.ProviderName,
                instruments.Count,
                eligible.Count,
                selectedActive.Count,
                added.Count,
                removed.Count,
                metadataUpdated.Count);

            return new MarketUniverseDiff
            {
                Added = added,
                Removed = removed,
                MetadataUpdated = metadataUpdated,
                Unchanged = unchanged
            };
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public string? ResolveNativeSymbol(string canonicalSymbol)
    {
        var match = _currentUniverse.Markets.FirstOrDefault(
            m => string.Equals(m.CanonicalSymbol, canonicalSymbol, StringComparison.OrdinalIgnoreCase));
        return match?.NativeSymbol;
    }

    private static bool HasMetadataChanged(MarketUniverseEntry prev, MarketUniverseEntry current)
    {
        return !string.Equals(prev.NativeSymbol, current.NativeSymbol, StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(prev.ContractStatus, current.ContractStatus, StringComparison.OrdinalIgnoreCase) ||
               prev.QuantoMultiplier != current.QuantoMultiplier;
    }
}
