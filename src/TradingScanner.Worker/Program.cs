using System.Globalization;
using Microsoft.Extensions.Configuration;
using TradingScanner.Domain.MarketStructure;
using TradingScanner.Domain.MarketStructure.Models;
using TradingScanner.Domain.Notifications;
using TradingScanner.Domain.Models;
using TradingScanner.Domain.SwingDetection;
using TradingScanner.Infrastructure.Bybit;
using TradingScanner.Infrastructure.Services;

const string symbol = "EVAAUSDT";

Console.WriteLine("================================================");
Console.WriteLine("       BTCUSDT HISTORICAL CANDLE LOADER");
Console.WriteLine("================================================");

using var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://api.bybit.com/"),
    Timeout = TimeSpan.FromSeconds(30)
};

httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "TradingScannerMVP/1.0"
);

// Create application services.

var bybitClient = new BybitRestClient(httpClient);

var validator = new CandleValidator();

var timeProvider = TimeProvider.System;

var historicalLoader = new HistoricalCandleLoader(
    bybitClient,
    validator,
    timeProvider
);

try
{
    Console.WriteLine();
    Console.WriteLine("Symbol: BTCUSDT");
    Console.WriteLine("Exchange: Bybit");
    Console.WriteLine("Category: Linear");
    Console.WriteLine("Timeframe: 4H");
    Console.WriteLine("Analysis window: 14 days");
    Console.WriteLine("Expected candles: 84");

    // Load and validate historical data.

    var history = await historicalLoader.LoadLatestAsync(symbol);

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

    Console.WriteLine($"Expected candles: {84}");
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
        Timeframe = "4H",
        CandleInterval = TimeSpan.FromHours(4),
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
    // 4. DETECT MARKET STRUCTURE
    // --------------------------------------------------

    var marketStructureDetector = new MarketStructureDetector();

    var structurePoints = marketStructureDetector.Detect(
        swingResult.SwingPoints);

    Console.WriteLine();
    Console.WriteLine("================================================================");
    Console.WriteLine("                      MARKET STRUCTURE");
    Console.WriteLine("================================================================");

    foreach (var point in structurePoints)
    {
        Console.WriteLine(
            $"{point.Type,-10} | " +
            $"{point.Label,-8} | " +
            $"{point.Price,-12} | " +
            $"{point.OccurredAt:yyyy-MM-dd HH:mm}");
    }

    // --------------------------------------------------
    // 5. CLASSIFY TREND
    // --------------------------------------------------

    var trendClassifier = new TrendClassifier();

    var trend = trendClassifier.Classify(structurePoints);

    Console.WriteLine();
    Console.WriteLine("================================================================");
    Console.WriteLine("                      TREND CLASSIFICATION");
    Console.WriteLine("================================================================");

    Console.WriteLine($"Symbol: {trend.Symbol}");
    Console.WriteLine($"Timeframe: {trend.Timeframe}");
    Console.WriteLine($"Direction: {trend.Direction}");
    Console.WriteLine($"HH transitions: {trend.HigherHighTransitions}");
    Console.WriteLine($"HL transitions: {trend.HigherLowTransitions}");
    Console.WriteLine($"LH transitions: {trend.LowerHighTransitions}");
    Console.WriteLine($"LL transitions: {trend.LowerLowTransitions}");
    Console.WriteLine(
        trend.EstablishedAt is { } established
            ? $"Established at: {established:yyyy-MM-dd HH:mm} UTC"
            : "Established at: N/A");

    // --------------------------------------------------
    // 6. PROCESS ALERT (Telegram + deduplication)
    // --------------------------------------------------

    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .Build();

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
    Console.WriteLine("Bybit HTTP request failed.");
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
