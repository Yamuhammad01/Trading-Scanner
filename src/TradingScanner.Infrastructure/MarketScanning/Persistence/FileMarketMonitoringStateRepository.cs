using System.Text.Json;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Infrastructure.MarketScanning.Persistence;

public sealed class FileMarketMonitoringStateRepository : IMarketMonitoringStateRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, MarketMonitoringState>? _states;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public FileMarketMonitoringStateRepository(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<MarketMonitoringState?> GetAsync(
        string provider,
        string canonicalSymbol,
        string timeframe,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(provider, canonicalSymbol, timeframe);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await LoadIfRequiredAsync(cancellationToken);
            return _states!.GetValueOrDefault(key);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<MarketMonitoringState>> GetAllAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await LoadIfRequiredAsync(cancellationToken);
            return _states!.Values
                .Where(s => string.Equals(s.Provider, provider.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.CanonicalSymbol, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(
        MarketMonitoringState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var key = BuildKey(state.Provider, state.CanonicalSymbol, state.Timeframe);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await LoadIfRequiredAsync(cancellationToken);
            _states![key] = state;
            await PersistAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string BuildKey(string provider, string symbol, string timeframe) =>
        $"{provider.Trim()}|{symbol.Trim().ToUpperInvariant()}|{timeframe.Trim().ToUpperInvariant()}";

    private async Task LoadIfRequiredAsync(CancellationToken cancellationToken)
    {
        if (_states is not null)
        {
            return;
        }

        if (!File.Exists(_filePath))
        {
            _states = new Dictionary<string, MarketMonitoringState>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            _states = new Dictionary<string, MarketMonitoringState>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var list = JsonSerializer.Deserialize<List<MarketMonitoringState>>(json, JsonOptions) ?? [];
        _states = new Dictionary<string, MarketMonitoringState>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            var key = BuildKey(item.Provider, item.CanonicalSymbol, item.Timeframe);
            _states[key] = item;
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_filePath))!;
        Directory.CreateDirectory(directory);

        var ordered = _states!
            .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kvp => kvp.Value)
            .ToList();

        var json = JsonSerializer.Serialize(ordered, JsonOptions);
        var tempPath = _filePath + ".tmp";

        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
