using TradingScanner.Domain.MarketStructure.Models;

namespace TradingScanner.Domain.Notifications;

public sealed class TrendAlertCoordinator
{
    private readonly TelegramNotificationService _telegram;
    private readonly AlertDeduplicationService _deduplication;

    public TrendAlertCoordinator(
        TelegramNotificationService telegram,
        AlertDeduplicationService deduplication)
    {
        _telegram = telegram;
        _deduplication = deduplication;
    }

    public async Task ProcessAsync(
        TrendResult trend,
        string dataProviderName,
        CancellationToken cancellationToken = default)
    {
        if (trend.Direction == TrendDirection.Neutral)
            return;

        var eventKey = TrendEventKeyBuilder.Build(trend);

        var alreadySent = await _deduplication.HasBeenSentAsync(
            eventKey,
            cancellationToken);

        if (alreadySent)
        {
            Console.WriteLine(
                $"Duplicate alert skipped: {trend.Symbol} " +
                $"{trend.Direction}");

            return;
        }

        await _telegram.SendTrendAlertAsync(
            trend,
            dataProviderName,
            cancellationToken);

        await _deduplication.MarkAsSentAsync(
            eventKey,
            cancellationToken);

        Console.WriteLine(
            $"Telegram alert sent: {trend.Symbol} " +
            $"{trend.Direction}");
    }
}