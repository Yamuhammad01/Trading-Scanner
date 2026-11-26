using System.Text.Json;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Infrastructure.TrendDetection;

public sealed class FileTrendStateRepository : ITrendStateRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, TrendState>? _states;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public FileTrendStateRepository(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public async Task<TrendState?> GetAsync(
        string provider,
        string symbol,
        string timeframe,
        string configurationId,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(provider, symbol, timeframe, configurationId);

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

    public async Task SaveAsync(
        TrendState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var key = BuildKey(state.Provider, state.Symbol, state.Timeframe, state.ConfigurationId);

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

    private static string BuildKey(
        string provider,
        string symbol,
        string timeframe,
        string configurationId) =>
        $"{provider.Trim()}|{symbol.Trim().ToUpperInvariant()}|{timeframe.Trim().ToUpperInvariant()}|{configurationId.Trim()}";

    private async Task LoadIfRequiredAsync(CancellationToken cancellationToken)
    {
        if (_states is not null)
        {
            return;
        }

        if (!File.Exists(_filePath))
        {
            _states = new Dictionary<string, TrendState>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            _states = new Dictionary<string, TrendState>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var list = JsonSerializer.Deserialize<List<TrendState>>(json, JsonOptions)
            ?? [];

        _states = new Dictionary<string, TrendState>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            var key = BuildKey(item.Provider, item.Symbol, item.Timeframe, item.ConfigurationId);
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
