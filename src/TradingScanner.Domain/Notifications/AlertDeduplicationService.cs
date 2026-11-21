using System.Text.Json;

namespace TradingScanner.Domain.Notifications;

public sealed class AlertDeduplicationService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private HashSet<string>? _sentEvents;

    public AlertDeduplicationService(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<bool> HasBeenSentAsync(
        string eventKey,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await LoadIfRequiredAsync(cancellationToken);

            return _sentEvents!.Contains(eventKey);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkAsSentAsync(
        string eventKey,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await LoadIfRequiredAsync(cancellationToken);

            if (!_sentEvents!.Add(eventKey))
                return;

            await SaveAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task LoadIfRequiredAsync(
        CancellationToken cancellationToken)
    {
        if (_sentEvents is not null)
            return;

        if (!File.Exists(_filePath))
        {
            _sentEvents = new HashSet<string>(
                StringComparer.Ordinal);

            return;
        }

        var json = await File.ReadAllTextAsync(
            _filePath,
            cancellationToken);

        var savedEvents =
            JsonSerializer.Deserialize<List<string>>(json)
            ?? new List<string>();

        _sentEvents = new HashSet<string>(
            savedEvents,
            StringComparer.Ordinal);
    }

    private async Task SaveAsync(
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(
            Path.GetFullPath(_filePath))!;

        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(
            _sentEvents!.OrderBy(x => x).ToList(),
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        var temporaryPath = _filePath + ".tmp";

        await File.WriteAllTextAsync(
            temporaryPath,
            json,
            cancellationToken);

        File.Move(
            temporaryPath,
            _filePath,
            overwrite: true);
    }
}