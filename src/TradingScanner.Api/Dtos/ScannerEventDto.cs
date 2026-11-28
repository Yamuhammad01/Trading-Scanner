namespace TradingScanner.Api.Dtos;

public sealed record ScannerEventDto
{
    public string Id { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string? Symbol { get; init; }
    public string? Provider { get; init; }
    public string? Timeframe { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Severity { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public object? Details { get; init; }
}