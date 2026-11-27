namespace TradingScanner.Domain.MarketScanning.Configuration;

public sealed class MarketMonitoringOptions
{
    public bool Enabled { get; set; } = true;
    public string[] Timeframes { get; set; } = ["4H"];
    public bool ClosedCandlesOnly { get; set; } = true;
    public int MaxConcurrentMarketProcessors { get; set; } = 10;
    public bool WebSocketEnabled { get; set; } = true;
    public bool RestReconciliationEnabled { get; set; } = true;
    public int RestReconciliationIntervalMinutes { get; set; } = 5;
    public int WebSocketReconnectBaseDelayMs { get; set; } = 1000;
    public int WebSocketReconnectMaxDelayMs { get; set; } = 30000;
    public int WebSocketPingIntervalSeconds { get; set; } = 15;
}
