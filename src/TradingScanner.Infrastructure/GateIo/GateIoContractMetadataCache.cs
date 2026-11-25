using System.Globalization;

namespace TradingScanner.Infrastructure.GateIo;

public sealed class GateIoContractMetadataCache
{
    private readonly Dictionary<string, CachedContract> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _cacheDuration;

    public GateIoContractMetadataCache(int cacheDurationMinutes)
    {
        _cacheDuration = TimeSpan.FromMinutes(cacheDurationMinutes);
    }

    public bool TryGet(string contract, out decimal quantoMultiplier)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(contract, out var cached) &&
                cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                quantoMultiplier = cached.QuantoMultiplier;
                return true;
            }

            quantoMultiplier = default;
            return false;
        }
    }

    public void Set(string contract, decimal quantoMultiplier)
    {
        lock (_cache)
        {
            _cache[contract] = new CachedContract(
                quantoMultiplier,
                DateTimeOffset.UtcNow.Add(_cacheDuration));
        }
    }

    private sealed record CachedContract(decimal QuantoMultiplier, DateTimeOffset ExpiresAt);
}
