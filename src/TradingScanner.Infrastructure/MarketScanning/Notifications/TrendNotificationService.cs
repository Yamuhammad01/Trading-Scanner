using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Events;
using TradingScanner.Domain.Notifications;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TradingScanner.Infrastructure.MarketScanning.Notifications;

public sealed class TrendNotificationService
{
    private readonly TelegramNotificationService? _telegram;
    private readonly AlertDeduplicationService? _deduplication;
    private readonly TelegramNotificationOptions _options;
    private readonly ILogger<TrendNotificationService> _logger;

    public TrendNotificationService(
        TelegramNotificationOptions options,
        TelegramNotificationService? telegram = null,
        AlertDeduplicationService? deduplication = null,
        ILogger<TrendNotificationService>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _telegram = telegram;
        _deduplication = deduplication;
        _logger = logger ?? NullLogger<TrendNotificationService>.Instance;
    }

    public async Task ProcessTrendUpdateAsync(
        TrendUpdatedEvent evt,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || _telegram is null || _deduplication is null)
        {
            return;
        }

        var eventType = ClassifyEvent(evt);
        if (eventType is null)
        {
            return;
        }

        if (!ShouldNotify(eventType))
        {
            return;
        }

        // Build deduplication key.
        var established = evt.TrendResult.EstablishedAt?.ToUniversalTime().ToString("O") ?? "unknown";
        var eventKey = $"{evt.Provider}|{evt.CanonicalSymbol}|{evt.Timeframe}|{eventType}|{established}";

        var alreadySent = await _deduplication.HasBeenSentAsync(eventKey, cancellationToken);
        if (alreadySent)
        {
            _logger.LogDebug(
                "Duplicate notification skipped: {Symbol} {EventType}",
                evt.CanonicalSymbol, eventType);
            return;
        }

        try
        {
            var message = BuildNotificationMessage(evt, eventType);
            await _telegram.SendTrendAlertAsync(evt.TrendResult, cancellationToken);
            await _deduplication.MarkAsSentAsync(eventKey, cancellationToken);

            _logger.LogInformation(
                "Trend notification sent: {Symbol} {Timeframe} {EventType}",
                evt.CanonicalSymbol, evt.Timeframe, eventType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Failed to send Telegram notification for {Symbol} {EventType}. Monitoring continues.",
                evt.CanonicalSymbol, eventType);
        }
    }

    private static string? ClassifyEvent(TrendUpdatedEvent evt)
    {
        // Invalidation: was directional, now Neutral/PotentialReversal.
        if (evt.PreviousDirection != TrendDirection.Neutral &&
            evt.Direction == TrendDirection.Neutral &&
            evt.Condition == TrendCondition.PotentialReversal)
        {
            return "Invalidated";
        }

        // Acceleration.
        if (evt.Condition == TrendCondition.Accelerating &&
            evt.PreviousCondition != TrendCondition.Accelerating)
        {
            return "Acceleration";
        }

        // Mature.
        if (evt.Condition == TrendCondition.Mature &&
            evt.PreviousCondition != TrendCondition.Mature)
        {
            return "Mature";
        }

        // New trend established (transition from Neutral/NoValidTrend to directional Established or higher).
        if (evt.PreviousDirection == TrendDirection.Neutral &&
            evt.Direction != TrendDirection.Neutral &&
            evt.Condition >= TrendCondition.Established)
        {
            return "TrendEstablished";
        }

        return null;
    }

    private bool ShouldNotify(string eventType) => eventType switch
    {
        "TrendEstablished" => _options.NotifyTrendEstablished,
        "Mature" => _options.NotifyTrendMature,
        "Acceleration" => _options.NotifyAcceleration,
        "Invalidated" => _options.NotifyInvalidation,
        _ => false
    };

    private static string BuildNotificationMessage(TrendUpdatedEvent evt, string eventType)
    {
        var trend = evt.TrendResult;
        var emoji = evt.Direction switch
        {
            TrendDirection.Bullish => "🟢",
            TrendDirection.Bearish => "🔴",
            _ => "⚪"
        };

        return $"""
        {emoji} GATE.IO TREND UPDATE

        Event: {eventType.ToUpperInvariant()}
        Symbol: {evt.CanonicalSymbol}
        Timeframe: {evt.Timeframe}
        Direction: {evt.Direction}
        Condition: {evt.Condition}

        Upper Norm Slope: {trend.UpperNormalizedSlope:N4} ATR/candle
        Lower Norm Slope: {trend.LowerNormalizedSlope:N4} ATR/candle
        Upper Boundary: {trend.UpperBoundaryPrice:N2}
        Lower Boundary: {trend.LowerBoundaryPrice:N2}
        ATR(14): {trend.Atr:N2}
        Retests: {trend.BoundaryRetestCount}
        Trend Established: {trend.EstablishedAt:yyyy-MM-dd HH:mm} UTC

        This is a trend-monitoring update, not a trade-entry signal.
        """;
    }
}
