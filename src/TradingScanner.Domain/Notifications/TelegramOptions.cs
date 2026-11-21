namespace TradingScanner.Domain.Notifications;

public sealed class TelegramOptions
{
    public required string BotToken { get; init; }

    public required string ChatId { get; init; }
}