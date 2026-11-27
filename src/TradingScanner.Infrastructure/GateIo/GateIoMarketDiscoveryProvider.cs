using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Infrastructure.GateIo;

public sealed class GateIoMarketDiscoveryProvider : IMarketDiscoveryProvider
{
    private readonly HttpClient _httpClient;
    private readonly GateIoMarketDataProviderOptions _options;
    private readonly GateIoContractMetadataCache _contractCache;
    private readonly ISymbolNormalizer _symbolNormalizer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GateIoMarketDiscoveryProvider> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public GateIoMarketDiscoveryProvider(
        HttpClient httpClient,
        GateIoMarketDataProviderOptions options,
        GateIoContractMetadataCache contractMetadataCache,
        ISymbolNormalizer? symbolNormalizer = null,
        TimeProvider? timeProvider = null,
        ILogger<GateIoMarketDiscoveryProvider>? logger = null)
    {
        _httpClient = httpClient;
        _options = options;
        _contractCache = contractMetadataCache;
        _symbolNormalizer = symbolNormalizer ?? new GateIoSymbolNormalizer();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<GateIoMarketDiscoveryProvider>.Instance;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new DecimalStringOrNumberConverter() }
        };
    }

    public string ProviderName => "Gate.io USDT Perpetual Futures";

    public async Task<IReadOnlyList<MarketInstrument>> GetAvailableMarketsAsync(
        CancellationToken cancellationToken = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/api/v4/futures/{_options.Settlement}/contracts";
        using var response = await GateIoHttpRetryHelper.SendWithRetryAsync(
            ct => _httpClient.GetAsync(url, ct),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var contracts = JsonSerializer.Deserialize<List<GateIoContractResponse>>(body, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize Gate.io contracts response.");

        var now = _timeProvider.GetUtcNow();
        var settlementUpper = _options.Settlement.ToUpperInvariant();
        var instruments = new List<MarketInstrument>(contracts.Count);

        foreach (var contract in contracts)
        {
            if (string.IsNullOrWhiteSpace(contract.Name))
            {
                _logger.LogWarning("Skipping Gate.io contract entry with empty name.");
                continue;
            }

            var nativeSymbol = contract.Name.Trim().ToUpperInvariant();
            var canonicalSymbol = _symbolNormalizer.ToCanonicalSymbol(nativeSymbol);
            _symbolNormalizer.RegisterMapping(canonicalSymbol, nativeSymbol);

            if (contract.QuantoMultiplier > 0)
            {
                _contractCache.Set(nativeSymbol, contract.QuantoMultiplier);
            }

            // Perpetual contracts on Gate.io have no expiry (expire_time is null or 0).
            DateTimeOffset? expiryTime = contract.ExpireTime is > 0
                ? DateTimeOffset.FromUnixTimeSeconds(contract.ExpireTime.Value)
                : null;
            var isPerpetual = !expiryTime.HasValue;

            // Active if not in delisting, not expired, and status is empty or "trading".
            var isStatusTrading = string.IsNullOrWhiteSpace(contract.Status) ||
                                  string.Equals(contract.Status, "trading", StringComparison.OrdinalIgnoreCase);
            var isExpired = expiryTime.HasValue && expiryTime.Value <= now;
            var isActive = !contract.InDelisting && isStatusTrading && !isExpired;

            instruments.Add(new MarketInstrument
            {
                Provider = ProviderName,
                CanonicalSymbol = canonicalSymbol,
                NativeSymbol = nativeSymbol,
                MarketType = MarketType.USDTPerpetual,
                SettlementCurrency = settlementUpper,
                IsPerpetual = isPerpetual,
                IsActive = isActive,
                Status = string.IsNullOrWhiteSpace(contract.Status) ? "trading" : contract.Status,
                InDelisting = contract.InDelisting,
                ExpiryTime = expiryTime,
                QuantoMultiplier = contract.QuantoMultiplier,
                DiscoveredAt = now
            });
        }

        return instruments;
    }

    public async Task<IReadOnlyList<MarketTicker>> GetMarketTickersAsync(
        CancellationToken cancellationToken = default)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/api/v4/futures/{_options.Settlement}/tickers";
        using var response = await GateIoHttpRetryHelper.SendWithRetryAsync(
            ct => _httpClient.GetAsync(url, ct),
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        var tickers = JsonSerializer.Deserialize<List<GateIoTickerResponse>>(body, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize Gate.io tickers response.");

        var now = _timeProvider.GetUtcNow();
        var result = new List<MarketTicker>(tickers.Count);

        foreach (var ticker in tickers)
        {
            if (string.IsNullOrWhiteSpace(ticker.Contract))
            {
                continue;
            }

            var nativeSymbol = ticker.Contract.Trim().ToUpperInvariant();
            var canonicalSymbol = _symbolNormalizer.ToCanonicalSymbol(nativeSymbol);

            // For USDT-settled perpetual futures, volume_24h_settle is the 24h turnover in USDT (settlement/quote currency).
            var quoteVolume = ticker.Volume24hSettle > 0m
                ? ticker.Volume24hSettle
                : ticker.Volume24hQuote;

            result.Add(new MarketTicker
            {
                Provider = ProviderName,
                NativeSymbol = nativeSymbol,
                CanonicalSymbol = canonicalSymbol,
                LastPrice = ticker.Last,
                MarkPrice = ticker.MarkPrice,
                QuoteVolume24h = quoteVolume,
                BaseVolume24h = ticker.Volume24hBase,
                ContractVolume24h = ticker.Volume24h,
                Timestamp = now
            });
        }

        return result;
    }
}
