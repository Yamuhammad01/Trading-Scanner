using System.Globalization;
using System.Text.Json;
using TradingScanner.Domain.Entities;

namespace TradingScanner.Infrastructure.Bybit;

public sealed class BybitRestClient
{
    private readonly HttpClient _httpClient;

    private const string Category = "linear";
    private const string Interval = "240";

    private const int RequestLimit = 100;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public BybitRestClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<Candle>> GetLatestClosedCandlesAsync(
        string symbol,
        int candleCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required.");

        if (candleCount <= 0)
            throw new ArgumentException(
                "Candle count must be greater than zero.");

        // 1. Construct the API request URL.
        var requestUrl =
            $"v5/market/kline" +
            $"?category={Category}" +
            $"&symbol={Uri.EscapeDataString(symbol)}" +
            $"&interval={Interval}" +
            $"&limit={RequestLimit}";

        Console.WriteLine($"Requesting: {_httpClient.BaseAddress}{requestUrl}");

        // 2. Send the HTTP GET request.
        using var response = await _httpClient.GetAsync(
            requestUrl,
            cancellationToken);

        // 3. Read the response body.
        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        // 4. Check HTTP status.
        response.EnsureSuccessStatusCode();

        // 5. Deserialize JSON into our response model.
        var apiResponse =
            JsonSerializer.Deserialize<BybitKlineResponse>(
                responseBody,
                JsonOptions);

        if (apiResponse is null)
            throw new InvalidOperationException(
                "Could not deserialize Bybit response.");

        // 6. Check Bybit's application-level response code.
        if (apiResponse.RetCode != 0)
        {
            throw new InvalidOperationException(
                $"Bybit API error: {apiResponse.RetCode} - " +
                apiResponse.RetMsg);
        }

        if (apiResponse.Result is null)
            throw new InvalidOperationException(
                "Bybit returned an empty result.");

        // 7. Convert the raw candle arrays into Candle objects.
        var candles = apiResponse.Result.List
            .Select(row => MapToCandle(row, symbol))
            .OrderBy(candle => candle.StartTime)
            .ToList();

        // 8. Exclude candles that have not completed.
        var now = DateTimeOffset.UtcNow;

        var closedCandles = candles
            .Where(candle =>
                candle.StartTime.AddHours(4) <= now)
            .ToList();

        // 9. Keep the latest requested number of closed candles.
        var latestCandles = closedCandles
            .TakeLast(candleCount)
            .ToList();

        return latestCandles;
    }

    private static Candle MapToCandle(
        string[] row,
        string symbol)
    {
        if (row.Length < 7)
        {
            throw new FormatException(
                "Invalid candle data returned by Bybit.");
        }

        var startTimeMilliseconds = long.Parse(
            row[0],
            CultureInfo.InvariantCulture);

        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(
            startTimeMilliseconds);

        return new Candle(
            Symbol: symbol,
            StartTime: startTime,
            Open: ParseDecimal(row[1]),
            High: ParseDecimal(row[2]),
            Low: ParseDecimal(row[3]),
            Close: ParseDecimal(row[4]),
            Volume: ParseDecimal(row[5]),
            Turnover: ParseDecimal(row[6])
        );
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.Parse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
    }
}