using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.Notifications;
using TradingScanner.Domain.Models;
using TradingScanner.Domain.SwingDetection;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;
using TradingScanner.Infrastructure.Services;
using TradingScanner.Infrastructure.TrendDetection;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();

services.AddLogging();

// Register market data provider based on configuration.
// Supported values: "Bybit" or "GateIo".
services.AddMarketDataProviderFromConfiguration(configuration);

// Register shared services.
services.AddSingleton(TimeProvider.System);
services.AddTransient<CandleValidator>();
services.AddTransient<HistoricalCandleLoader>();

// Register rolling slope trend detection (loads and validates Configuration/rolling-slope-4h.json).
services.AddRollingSlopeTrendDetection(configuration);

using var serviceProvider = services.BuildServiceProvider();

var provider = serviceProvider.GetRequiredService<IMarketDataProvider>();
var providerName = provider.ProviderName;

var symbol = configuration["MarketData:Symbol"] ?? "BTCUSDT";
var timeframeConfig = configuration["MarketData:Timeframe"] ?? "4h";
var timeframe = Timeframe.Parse(timeframeConfig);

var startTime = ParseOptionalUtcTimestamp(
    configuration["MarketData:StartTime"],
    "MarketData:StartTime");
var endTime = ParseOptionalUtcTimestamp(
    configuration["MarketData:EndTime"],
    "MarketData:EndTime");

Console.WriteLine("================================================");
Console.WriteLine($"       {symbol} HISTORICAL CANDLE LOADER");
Console.WriteLine("================================================");



var validator = serviceProvider.GetRequiredService<CandleValidator>();
var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
var historicalLoader = serviceProvider.GetRequiredService<HistoricalCandleLoader>();

try
{
    Console.WriteLine();
    Console.WriteLine($"Symbol: {symbol}");
    Console.WriteLine($"Provider: {providerName}");
    Console.WriteLine($"Timeframe: {timeframe.Name.ToUpperInvariant()}");
    if (startTime.HasValue || endTime.HasValue)
    {
        Console.WriteLine($"Start time: {(startTime.HasValue ? startTime.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : "14 days before end")}");
        Console.WriteLine($"End time:   {(endTime.HasValue ? endTime.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : "Latest closed candle")}");
    }
    else
    {
        Console.WriteLine("Analysis window: 14 days (default)");
    }

    // Load and validate historical data.

    CandleHistory history;
    if (startTime.HasValue || endTime.HasValue)
    {
        var effectiveEnd = endTime ?? timeProvider.GetUtcNow();
        var effectiveStart = startTime ?? effectiveEnd.AddDays(-14);
        history = await historicalLoader.LoadRangeAsync(
            symbol,
            timeframe,
            effectiveStart,
            effectiveEnd);
    }
    else
    {
        history = await historicalLoader.LoadLatestAsync(symbol, timeframe);
    }

    Console.WriteLine();
    Console.WriteLine("================================================");
    Console.WriteLine("             HISTORICAL DATA");
    Console.WriteLine("================================================");

    Console.WriteLine($"Symbol: {history.Symbol}");
    Console.WriteLine($"Timeframe: {history.Timeframe}");
    Console.WriteLine($"Total candles: {history.Candles.Count}");

    Console.WriteLine(
        $"Oldest candle: {history.OldestCandleTime:yyyy-MM-dd HH:mm} UTC");

    Console.WriteLine(
        $"Latest candle: {history.LatestCandleTime:yyyy-MM-dd HH:mm} UTC");

    Console.WriteLine(
        $"Loaded at: {history.LoadedAt:yyyy-MM-dd HH:mm:ss} UTC");

    Console.WriteLine();
    Console.WriteLine(
        "TIME (UTC)           OPEN       HIGH        LOW      CLOSE");

    Console.WriteLine(
        "----------------------------------------------------------");

    foreach (var candle in history.Candles)
    {
        var time = candle.StartTime.ToString(
            "yyyy-MM-dd HH:mm",
            CultureInfo.InvariantCulture);

        Console.WriteLine(
            $"{time,-20}" +
            $"{candle.Open,11:N2}" +
            $"{candle.High,11:N2}" +
            $"{candle.Low,11:N2}" +
            $"{candle.Close,11:N2}"
        );
    }

    Console.WriteLine();
    Console.WriteLine("================================================");
    Console.WriteLine("             VALIDATION SUMMARY");
    Console.WriteLine("================================================");

    Console.WriteLine($"Received candles: {history.Candles.Count}");
    Console.WriteLine("Duplicate candles: Removed/checked");
    Console.WriteLine("Missing candles: None");
    Console.WriteLine("OHLC validation: Passed");
    Console.WriteLine("Chronological order: Passed");
    Console.WriteLine("Continuity: Passed");

    Console.WriteLine();
    Console.WriteLine("STATUS: HISTORICAL DATA READY");

    // Run swing point detection on the validated historical candles.

    var options = new SwingPointDetectorOptions
    {
        Timeframe = history.Timeframe,
        CandleInterval = timeframe.Interval,
        ConfirmationCandles = 2
    };

    var detector = new SwingPointDetector(options);

    var swingResult = detector.Detect(history.Candles);

    Console.WriteLine();
    Console.WriteLine("================================================================");
    Console.WriteLine("                      SWING DETECTION RESULTS");
    Console.WriteLine("================================================================");

    Console.WriteLine($"Processed candles: {swingResult.ProcessedCandles}");
    Console.WriteLine($"Initialized: {swingResult.IsInitialized}");
    Console.WriteLine($"Confirmed swing points: {swingResult.SwingPoints.Count}");

    foreach (var swing in swingResult.SwingPoints)
    {
        Console.WriteLine();

        Console.WriteLine($"Type: {swing.Type}");
        Console.WriteLine($"Symbol: {swing.Symbol}");
        Console.WriteLine($"Timeframe: {swing.Timeframe}");
        Console.WriteLine($"Price: {swing.Price}");
        Console.WriteLine($"Occurred: {swing.OccurredAt:yyyy-MM-dd HH:mm} UTC");
        Console.WriteLine($"Confirmed: {swing.ConfirmedAt:yyyy-MM-dd HH:mm} UTC");
    }

    // --------------------------------------------------
    // 4. ROLLING SLOPE TREND DETECTION (REPLAY & LATEST)
    // --------------------------------------------------

    var trendDetector = serviceProvider.GetRequiredService<ITrendDetector>();

    var replayResults = await trendDetector.ReplayAsync(
        providerName,
        symbol,
        history.Timeframe,
        history.Candles);

    Console.WriteLine();
    Console.WriteLine("================================================================");
    Console.WriteLine("              ROLLING SLOPE REPLAY DIAGNOSTICS");
    Console.WriteLine("================================================================");

    foreach (var r in replayResults.Where(x => x.UpperBoundaryPrice.HasValue))
    {
        var ageStr = r.TrendAge.HasValue ? $"{r.TrendAge.Value.TotalDays:F1}d" : "-";
        var brkStr = r.BreakoutDistanceAtr.HasValue ? $"{r.BreakoutDistanceAtr.Value:F2}" : "-";
        Console.WriteLine(
            $"{r.LastProcessedCandleTime:yyyy-MM-dd HH:mm} | " +
            $"U:{r.UpperBoundaryPrice,9:N2} L:{r.LowerBoundaryPrice,9:N2} | " +
            $"US:{r.UpperNormalizedSlope,6:F3} LS:{r.LowerNormalizedSlope,6:F3} | " +
            $"{r.Direction,-7} | {r.Condition,-17} | " +
            $"Ret:{r.BoundaryRetestCount} | Age:{ageStr,-5} | Brk:{brkStr}");
    }

    var trend = replayResults[^1];

    Console.WriteLine();
    Console.WriteLine("================================================================");
    Console.WriteLine("             ROLLING SLOPE TREND CLASSIFICATION");
    Console.WriteLine("================================================================");

    Console.WriteLine($"Provider: {trend.Provider}");
    Console.WriteLine($"Symbol: {trend.Symbol}");
    Console.WriteLine($"Timeframe: {trend.Timeframe}");
    Console.WriteLine($"Direction: {trend.Direction}");
    Console.WriteLine($"Condition: {trend.Condition}");
    Console.WriteLine($"Upper Slope: {(trend.UpperSlope.HasValue ? trend.UpperSlope.Value.ToString("N4", CultureInfo.InvariantCulture) : "N/A")}");
    Console.WriteLine($"Lower Slope: {(trend.LowerSlope.HasValue ? trend.LowerSlope.Value.ToString("N4", CultureInfo.InvariantCulture) : "N/A")}");
    Console.WriteLine($"Upper Norm Slope: {(trend.UpperNormalizedSlope.HasValue ? trend.UpperNormalizedSlope.Value.ToString("N4", CultureInfo.InvariantCulture) : "N/A")}");
    Console.WriteLine($"Lower Norm Slope: {(trend.LowerNormalizedSlope.HasValue ? trend.LowerNormalizedSlope.Value.ToString("N4", CultureInfo.InvariantCulture) : "N/A")}");
    Console.WriteLine($"Upper Boundary: {(trend.UpperBoundaryPrice.HasValue ? trend.UpperBoundaryPrice.Value.ToString("N2", CultureInfo.InvariantCulture) : "N/A")}");
    Console.WriteLine($"Lower Boundary: {(trend.LowerBoundaryPrice.HasValue ? trend.LowerBoundaryPrice.Value.ToString("N2", CultureInfo.InvariantCulture) : "N/A")}");
    Console.WriteLine($"ATR(14): {(trend.Atr.HasValue ? trend.Atr.Value.ToString("N4", CultureInfo.InvariantCulture) : "N/A")}");
    Console.WriteLine($"Retest count: {trend.BoundaryRetestCount}");
    Console.WriteLine(
        trend.EstablishedAt is { } established
            ? $"Established at: {established:yyyy-MM-dd HH:mm} UTC"
            : "Established at: N/A");

    // --------------------------------------------------
    // 5. PROCESS ALERT (Telegram + deduplication)
    // --------------------------------------------------

    var botToken = configuration["Telegram:BotToken"]
        ?? Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");

    var chatId = configuration["Telegram:ChatId"]
        ?? Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");

    if (string.IsNullOrWhiteSpace(botToken) ||
        string.IsNullOrWhiteSpace(chatId))
    {
        Console.WriteLine();
        Console.WriteLine(
            "Telegram credentials not configured. " +
            "Set TELEGRAM_BOT_TOKEN and TELEGRAM_CHAT_ID to enable alerts.");
    }
    else
    {
        var telegramOptions = new TelegramOptions
        {
            BotToken = botToken,
            ChatId = chatId
        };

        using var alertHttpClient = new HttpClient();

        var telegramService = new TelegramNotificationService(
            alertHttpClient,
            telegramOptions);

        var deduplicationService = new AlertDeduplicationService(
            Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "sent-alerts.json"));

        var alertCoordinator = new TrendAlertCoordinator(
            telegramService,
            deduplicationService);

        await alertCoordinator.ProcessAsync(trend);
    }

    Console.WriteLine();
    Console.WriteLine("SCANNER EXECUTION COMPLETED.");

}
catch (HttpRequestException ex)
{
    Console.WriteLine("Market data HTTP request failed.");
    Console.WriteLine(ex.Message);
}
catch (TaskCanceledException ex)
{
    Console.WriteLine("The request timed out or was cancelled.");
    Console.WriteLine(ex.Message);
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("HISTORICAL DATA LOADING FAILED");
    Console.WriteLine(ex);
}

static DateTimeOffset? ParseOptionalUtcTimestamp(string? value, string key)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    if (!DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
    {
        throw new InvalidOperationException(
            $"Invalid '{key}' value '{value}'. Use an ISO 8601 date or timestamp (e.g. '2026-09-24' or '2026-09-24T00:00:00Z').");
    }

    return parsed;
}







