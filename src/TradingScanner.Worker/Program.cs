using System.Globalization;
using TradingScanner.Infrastructure.Bybit;

Console.WriteLine("=============================================");
Console.WriteLine("       BYBIT HISTORICAL CANDLE LOADER");
Console.WriteLine("=============================================");

const string symbol = "BTCUSDT";
const int candleCount = 84;

using var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://api.bybit.com/"),
    Timeout = TimeSpan.FromSeconds(120)
};

httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "TradingScannerMVP/1.0"
);

var bybitClient = new BybitRestClient(httpClient);

try
{
    Console.WriteLine();
    Console.WriteLine($"Exchange:       Bybit");
    Console.WriteLine($"Market:         {symbol}");
    Console.WriteLine($"Category:       Linear (USDT Perpetual)");
    Console.WriteLine($"Timeframe:      4 Hours");
    Console.WriteLine($"Requested:      {candleCount} closed candles");
    Console.WriteLine();

    var candles = await bybitClient.GetLatestClosedCandlesAsync(
        symbol,
        candleCount
    );

    if (candles.Count == 0)
    {
        Console.WriteLine("No closed candles were returned.");
        return;
    }

    Console.WriteLine(
        $"Successfully retrieved {candles.Count} candles.");

    Console.WriteLine();

    Console.WriteLine(
        "TIME (UTC)           OPEN       HIGH        LOW      CLOSE");

    Console.WriteLine(
        "----------------------------------------------------------");

    foreach (var candle in candles)
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

    Console.WriteLine(
        "----------------------------------------------------------");

    Console.WriteLine($"Total candles: {candles.Count}");

    Console.WriteLine(
        $"Oldest candle: {candles.First().StartTime:yyyy-MM-dd HH:mm} UTC");

    Console.WriteLine(
        $"Latest candle: {candles.Last().StartTime:yyyy-MM-dd HH:mm} UTC");

    Console.WriteLine("Status: SUCCESS");
}
catch (HttpRequestException ex)
{
    Console.WriteLine("HTTP request failed.");
    Console.WriteLine(ex.Message);
}
catch (TaskCanceledException ex)
{
    Console.WriteLine("The API request timed out or was cancelled.");
    Console.WriteLine(ex.Message);
}
catch (Exception ex)
{
    Console.WriteLine("An unexpected error occurred.");
    Console.WriteLine(ex);
}
