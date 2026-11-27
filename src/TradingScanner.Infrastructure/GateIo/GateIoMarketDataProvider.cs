using System.Globalization;
using System.Text.Json;
using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.MarketScanning.Abstractions;

namespace TradingScanner.Infrastructure.GateIo;

public sealed class GateIoMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly GateIoMarketDataProviderOptions _options;
    private readonly GateIoContractMetadataCache _contractCache;
    private readonly ISymbolNormalizer _symbolNormalizer;
    private readonly JsonSerializerOptions _jsonOptions;

    public GateIoMarketDataProvider(
        HttpClient httpClient,
        GateIoMarketDataProviderOptions options,
        GateIoContractMetadataCache contractMetadataCache,
        ISymbolNormalizer? symbolNormalizer = null)
    {
        _httpClient = httpClient;
        _options = options;
        _contractCache = contractMetadataCache;
        _symbolNormalizer = symbolNormalizer ?? new GateIoSymbolNormalizer();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new DecimalStringOrNumberConverter() },
        };
    }

    public string ProviderName => "Gate.io USDT Perpetual Futures";

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        MarketDataRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MarketType != MarketType.USDTPerpetual)
        {
            throw new NotSupportedException(
                $"Gate.io provider only supports {nameof(MarketType.USDTPerpetual)} market type.");
        }

        var contract = _symbolNormalizer.ToNativeSymbol(request.Symbol);
        var interval = MapInterval(request.Timeframe);

        long? fromSeconds = null;
        long? toSeconds = null;
        int? limit = null;

        if (request.StartTime.HasValue || request.EndTime.HasValue)
        {
            fromSeconds = request.StartTime?.ToUnixTimeSeconds();
            toSeconds = request.EndTime?.ToUnixTimeSeconds();
        }
        else if (request.Limit.HasValue)
        {
            limit = request.Limit.Value;
        }

        var url = BuildCandlesticksUrl(
            contract,
            interval,
            fromSeconds,
            toSeconds,
            limit);

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        response.EnsureSuccessStatusCode();

        var candles =
            JsonSerializer.Deserialize<List<GateIoCandlestickResponse>>(
                responseBody,
                _jsonOptions) ?? new List<GateIoCandlestickResponse>();

        var result = new List<Candle>(candles.Count);

        foreach (var candle in candles)
        {
            var quantoMultiplier = await GetQuantoMultiplierAsync(
                contract,
                cancellationToken);

            var turnover = ParseTurnover(candle.Turnover, candle.Timestamp);

            result.Add(new Candle(
                Symbol: request.Symbol,
                StartTime: DateTimeOffset.FromUnixTimeSeconds(candle.Timestamp),
                Open: candle.Open,
                High: candle.High,
                Low: candle.Low,
                Close: candle.Close,
                Volume: candle.ContractVolume * quantoMultiplier,
                Turnover: turnover));
        }

        return result
            .OrderBy(c => c.StartTime)
            .ToList();
    }

    private static string MapSymbol(string canonicalSymbol)
    {
        var upper = canonicalSymbol.ToUpperInvariant();

        if (upper.Contains("_"))
        {
            return upper;
        }

        if (upper.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
        {
            var baseAsset = upper[..^4];
            return $"{baseAsset}_USDT";
        }

        return upper;
    }

    private static string MapInterval(Timeframe timeframe)
    {
        return timeframe.Name switch
        {
            "5m" => "5m",
            "1h" => "1h",
            "4h" => "4h",
            _ => throw new NotSupportedException(
                $"Gate.io does not support interval '{timeframe.Name}'."),
        };
    }

    private string BuildCandlesticksUrl(
        string contract,
        string interval,
        long? fromSeconds,
        long? toSeconds,
        int? limit)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}" +
                  $"/api/v4/futures/{_options.Settlement}/candlesticks" +
                  $"?contract={Uri.EscapeDataString(contract)}" +
                  $"&interval={Uri.EscapeDataString(interval)}";

        if (fromSeconds.HasValue)
        {
            url += $"&from={fromSeconds.Value}";
        }

        if (toSeconds.HasValue)
        {
            url += $"&to={toSeconds.Value}";
        }

        if (limit.HasValue)
        {
            url += $"&limit={limit.Value}";
        }

        return url;
    }

    private async Task<decimal> GetQuantoMultiplierAsync(
        string contract,
        CancellationToken cancellationToken)
    {
        if (_contractCache.TryGet(contract, out var multiplier))
        {
            return multiplier;
        }

        var url = $"{_options.BaseUrl.TrimEnd('/')}" +
                  $"/api/v4/futures/{_options.Settlement}/contracts/" +
                  $"{Uri.EscapeDataString(contract)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        response.EnsureSuccessStatusCode();

        var contractInfo =
            JsonSerializer.Deserialize<GateIoContractResponse>(
                responseBody,
                _jsonOptions);

        if (contractInfo is null)
        {
            throw new InvalidOperationException(
                $"Could not deserialize contract metadata for {contract}.");
        }

        if (contractInfo.QuantoMultiplier <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid quanto_multiplier for contract {contract}: " +
                $"{contractInfo.QuantoMultiplier}.");
        }

        _contractCache.Set(contract, contractInfo.QuantoMultiplier);

        return contractInfo.QuantoMultiplier;
    }

    private static decimal ParseTurnover(string? turnover, long timestamp)
    {
        if (string.IsNullOrWhiteSpace(turnover))
        {
            throw new InvalidOperationException(
                $"Missing turnover (sum) for candle at {timestamp}.");
        }

        if (!decimal.TryParse(
                turnover,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new InvalidOperationException(
                $"Invalid turnover (sum) value '{turnover}' for candle at {timestamp}.");
        }

        return parsed;
    }
}
