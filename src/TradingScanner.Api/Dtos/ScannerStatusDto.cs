namespace TradingScanner.Api.Dtos;

public sealed record ScannerStatusDto
{
    public string OverallState { get; init; } = string.Empty;
    public string BackgroundWorkerState { get; init; } = string.Empty;
    public string ActiveProvider { get; init; } = string.Empty;
    public int MonitoredMarketCount { get; init; }
    public int ActiveSubscriptions { get; init; }
    public DateTimeOffset? LastSuccessfulMarketDataUpdate { get; init; }
    public bool WebSocketConnected { get; init; }
    public string? ReconnectionStatus { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset? ScannerStartTime { get; init; }
    public TimeSpan? Uptime { get; init; }
}