using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TradingScanner.Domain.MarketStructure.Models;
using TradingScanner.Domain.SwingDetection.Models;

namespace TradingScanner.Domain.Notifications;

public sealed class TelegramNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _options;

    public TelegramNotificationService(
        HttpClient httpClient,
        TelegramOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task SendTrendAlertAsync(
        TrendResult trend,
        string dataProviderName,
        CancellationToken cancellationToken = default)
    {
        if (trend.Direction == TrendDirection.Neutral)
            return;

        var message = BuildMessage(trend, dataProviderName);

        var request = new TelegramMessageRequest
        {
            ChatId = _options.ChatId,
            Text = message
        };

        var url =
            $"https://api.telegram.org/bot{_options.BotToken}/sendMessage";

        using var response = await _httpClient.PostAsJsonAsync(
            url,
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<TelegramApiResponse>(
                cancellationToken: cancellationToken);

        if (result?.Ok != true)
        {
            throw new InvalidOperationException(
                "Telegram rejected the notification.");
        }
    }

    private static string BuildMessage(TrendResult trend, string dataProviderName)
    {
        var bullish = trend.Direction == TrendDirection.Bullish;

        var title = bullish
            ? "🟢 BULLISH TREND DETECTED"
            : "🔴 BEARISH TREND DETECTED";

        var highLabel = bullish ? "HH" : "LH";
        var lowLabel = bullish ? "HL" : "LL";

        var recentPoints = trend.StructurePoints;

        var highs = recentPoints
            .Where(x => x.Type == SwingPointType.SwingHigh)
            .Select(x => x.Price)
            .ToArray();

        var lows = recentPoints
            .Where(x => x.Type == SwingPointType.SwingLow)
            .Select(x => x.Price)
            .ToArray();

        return $$"""
        {title}

        Symbol: {trend.Symbol}
        Exchange: {dataProviderName}
        Timeframe: {trend.Timeframe}

        Market Structure:
        • {trend.HigherHighTransitions + trend.LowerHighTransitions} consecutive {highLabel} transitions
        • {trend.HigherLowTransitions + trend.LowerLowTransitions} consecutive {lowLabel} transitions

        Swing Highs: {string.Join(" → ", highs)}
        Swing Lows: {string.Join(" → ", lows)}

        Trend: {trend.Direction.ToString().ToUpperInvariant()}
        Confirmed: {trend.EstablishedAt:yyyy-MM-dd HH:mm} UTC
        """;
    }

    private sealed class TelegramMessageRequest
    {
        [JsonPropertyName("chat_id")]
        public required string ChatId { get; init; }

        [JsonPropertyName("text")]
        public required string Text { get; init; }
    }

    private sealed class TelegramApiResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; init; }
    }
}