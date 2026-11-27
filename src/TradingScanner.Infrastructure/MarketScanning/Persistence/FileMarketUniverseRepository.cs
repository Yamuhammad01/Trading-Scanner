using System.Text.Json;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Infrastructure.MarketScanning.Persistence;

public sealed class FileMarketUniverseRepository : IMarketUniverseRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public FileMarketUniverseRepository(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<MarketUniverse?> GetAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAllAsync(cancellationToken);
            return all.TryGetValue(provider.Trim(), out var universe) ? universe : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(
        MarketUniverse universe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(universe);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var all = await LoadAllAsync(cancellationToken);
            all[universe.Provider.Trim()] = universe;
            await PersistAllAsync(all, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, MarketUniverse>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, MarketUniverse>(StringComparer.OrdinalIgnoreCase);
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, MarketUniverse>(StringComparer.OrdinalIgnoreCase);
        }

        var list = JsonSerializer.Deserialize<List<MarketUniverse>>(json, JsonOptions) ?? [];
        var dict = new Dictionary<string, MarketUniverse>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            dict[item.Provider.Trim()] = item;
        }

        return dict;
    }

    private async Task PersistAllAsync(
        Dictionary<string, MarketUniverse> all,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath))!;
        Directory.CreateDirectory(directory);

        var ordered = all
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => kvp.Value)
            .ToList();

        var json = JsonSerializer.Serialize(ordered, JsonOptions);
        var tempPath = _filePath + ".tmp";

        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
