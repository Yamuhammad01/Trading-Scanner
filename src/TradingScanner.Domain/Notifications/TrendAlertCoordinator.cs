using TradingScanner.Domain.MarketStructure.Models;
using RollingSlopeTrendDirection = TradingScanner.Domain.TrendDetection.RollingSlope.Models.TrendDirection;
using RollingSlopeTrendResult = TradingScanner.Domain.TrendDetection.RollingSlope.Models.TrendResult;

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

    public async Task ProcessAsync(
        RollingSlopeTrendResult trend,
        CancellationToken cancellationToken = default)
    {
        if (trend.Direction == RollingSlopeTrendDirection.Neutral)
            return;

        var eventKey = TrendEventKeyBuilder.Build(trend);

        var alreadySent = await _deduplication.HasBeenSentAsync(
            eventKey,
            cancellationToken);

        if (alreadySent)
        {
            Console.WriteLine(
                $"Duplicate alert skipped: {trend.Symbol} " +
                $"{trend.Direction} ({trend.Condition})");

            return;
        }

        await _telegram.SendTrendAlertAsync(
            trend,
            cancellationToken);

        await _deduplication.MarkAsSentAsync(
            eventKey,
            cancellationToken);

        Console.WriteLine(
            $"Telegram alert sent: {trend.Symbol} " +
            $"{trend.Direction} ({trend.Condition})");
    }
}