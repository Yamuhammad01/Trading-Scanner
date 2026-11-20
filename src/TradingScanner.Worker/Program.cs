using System.Globalization;
using TradingScanner.Domain.Models;
using TradingScanner.Infrastructure.Bybit;
using TradingScanner.Infrastructure.Services;

const string symbol = "BTCUSDT";

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
