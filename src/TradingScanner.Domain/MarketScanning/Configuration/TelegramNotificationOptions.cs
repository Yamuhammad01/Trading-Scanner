namespace TradingScanner.Domain.MarketScanning.Configuration;

public sealed class TelegramNotificationOptions
{
    public bool Enabled { get; set; } = true;
    public bool NotifyTrendEstablished { get; set; } = true;
    public bool NotifyTrendMature { get; set; } = true;
    public bool NotifyAcceleration { get; set; } = true;
    public bool NotifyInvalidation { get; set; } = true;
}
