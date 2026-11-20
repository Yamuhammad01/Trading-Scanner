using System.Globalization;
using System.Text.Json;
using TradingScanner.Domain.Entities;

namespace TradingScanner.Infrastructure.Bybit;

public sealed class BybitRestClient
{
    private readonly HttpClient _httpClient;

    private const string Category = "linear";
    private const string Interval = "240";

    public BybitRestClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string symbol,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var start = startTime.ToUnixTimeMilliseconds();
        var end = endTime.ToUnixTimeMilliseconds();

        var requestUrl =
            $"v5/market/kline" +
            $"?category={Category}" +
            $"&symbol={Uri.EscapeDataString(symbol)}" +
            $"&interval={Interval}" +
            $"&start={start}" +
            $"&end={end}" +
            $"&limit={limit}";

        using var response = await _httpClient.GetAsync(
            requestUrl,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var apiResponse =
            JsonSerializer.Deserialize<BybitKlineResponse>(
                responseBody);

        if (apiResponse is null)
        {
            throw new InvalidOperationException(
                "Could not deserialize Bybit response.");
        }

        if (apiResponse.RetCode != 0)
        {
            throw new InvalidOperationException(
                $"Bybit API error: {apiResponse.RetCode} - " +
                apiResponse.RetMsg);
        }

        if (apiResponse.Result is null)
        {
            throw new InvalidOperationException(
                "Bybit returned an empty result.");
        }

        return apiResponse.Result.List
            .Select(row => MapToCandle(row, symbol))
            .OrderBy(candle => candle.StartTime)
            .ToList();
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

        var timestamp = long.Parse(
            row[0],
            CultureInfo.InvariantCulture);

        return new Candle(
            Symbol: symbol,
            StartTime: DateTimeOffset.FromUnixTimeMilliseconds(timestamp),
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