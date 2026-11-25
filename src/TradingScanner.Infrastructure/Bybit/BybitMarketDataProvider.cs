using System.Globalization;
using System.Text.Json;
using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketData;

namespace TradingScanner.Infrastructure.Bybit;

public sealed class BybitMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly BybitMarketDataProviderOptions _options;

    public BybitMarketDataProvider(
        HttpClient httpClient,
        BybitMarketDataProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string ProviderName => "Bybit";

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        MarketDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MarketType != MarketType.USDTPerpetual)
        {
            throw new NotSupportedException(
                $"Bybit provider only supports {nameof(MarketType.USDTPerpetual)} market type.");
        }

        var interval = MapInterval(request.Timeframe);

        var startMs = request.StartTime?.ToUnixTimeMilliseconds();
        var endMs = request.EndTime?.ToUnixTimeMilliseconds();

        var url = BuildRequestUrl(request.Symbol, interval, startMs, endMs, request.Limit);

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        response.EnsureSuccessStatusCode();

        var apiResponse = JsonSerializer.Deserialize<BybitKlineResponse>(responseBody);

        if (apiResponse is null)
        {
            throw new InvalidOperationException("Could not deserialize Bybit response.");
        }

        if (apiResponse.RetCode != 0)
        {
            throw new InvalidOperationException(
                $"Bybit API error: {apiResponse.RetCode} - {apiResponse.RetMsg}");
        }

        if (apiResponse.Result is null)
        {
            throw new InvalidOperationException("Bybit returned an empty result.");
        }

        return apiResponse.Result.List
            .Select(row => MapToCandle(row, request.Symbol))
            .OrderBy(candle => candle.StartTime)
            .ToList();
    }

    private static string MapInterval(Timeframe timeframe)
    {
        return timeframe.Name switch
        {
            "5m" => "5",
            "1h" => "60",
            "4h" => "240",
            _ => throw new NotSupportedException(
                $"Bybit does not support interval '{timeframe.Name}'."),
        };
    }

    private string BuildRequestUrl(
        string symbol,
        string interval,
        long? startMs,
        long? endMs,
        int? limit)
    {
        var url = $"v5/market/kline?category={_options.Category}&symbol=" +
                  $"{Uri.EscapeDataString(symbol)}&interval={interval}";

        if (startMs.HasValue)
        {
            url += $"&start={startMs.Value}";
        }

        if (endMs.HasValue)
        {
            url += $"&end={endMs.Value}";
        }

        if (limit.HasValue)
        {
            url += $"&limit={limit.Value}";
        }

        return url;
    }

    private static Candle MapToCandle(string[] row, string symbol)
    {
        if (row.Length < 7)
        {
            throw new FormatException("Invalid candle data returned by Bybit.");
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
            Turnover: ParseDecimal(row[6]));
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.Parse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
    }
}
