using Microsoft.Extensions.Logging;
using TradingScanner.Domain.MarketData;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Models;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;
using TradingScanner.Api.Dtos;

namespace TradingScanner.Api.Services;

public sealed class MarketDataQueryService
{
    private readonly IMarketUniverseManager _universeManager;
    private readonly IMarketMonitoringStateRepository _monitoringStateRepository;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly ILogger<MarketDataQueryService> _logger;

    public MarketDataQueryService(
        IMarketUniverseManager universeManager,
        IMarketMonitoringStateRepository monitoringStateRepository,
        IMarketDataProvider marketDataProvider,
        ILogger<MarketDataQueryService> logger)
    {
        _universeManager = universeManager;
        _monitoringStateRepository = monitoringStateRepository;
        _marketDataProvider = marketDataProvider;
        _logger = logger;
    }

    public MarketOverviewDto GetDashboardOverview()
    {
        var universe = _universeManager.CurrentUniverse;
        var allMarkets = universe.Markets;
        var monitoringStates = _monitoringStateRepository.GetAllAsync(
            _marketDataProvider.ProviderName).GetAwaiter().GetResult();

        var initializedCount = monitoringStates.Count(s => s.IsInitialized);
        var monitoringCount = monitoringStates.Count(s => s.IsActive);
        var failedCount = monitoringStates.Count(s => !string.IsNullOrWhiteSpace(s.LastError));

        var bullishCount = monitoringStates.Count(s => s.TrendDirection == TrendDirection.Bullish);
        var bearishCount = monitoringStates.Count(s => s.TrendDirection == TrendDirection.Bearish);
        var neutralCount = monitoringStates.Count(s => s.TrendDirection == TrendDirection.Neutral);

        var developingCount = monitoringStates.Count(s => s.TrendCondition == TrendCondition.Developing);
        var acceleratingCount = monitoringStates.Count(s => s.TrendCondition == TrendCondition.Accelerating);
        var matureCount = monitoringStates.Count(s => s.TrendCondition == TrendCondition.Mature);

        var lastUpdate = monitoringStates
            .Where(s => s.LastReceivedDataTime.HasValue)
            .MaxBy(s => s.LastReceivedDataTime.Value)?
            .LastReceivedDataTime;

        return new MarketOverviewDto
        {
            TotalDiscoveredMarkets = allMarkets.Count,
            TotalInitializedMarkets = initializedCount,
            TotalActivelyMonitoredMarkets = monitoringCount,
            BullishMarketCount = bullishCount,
            BearishMarketCount = bearishCount,
            NeutralMarketCount = neutralCount,
            DevelopingTrendCount = developingCount,
            AcceleratingTrendCount = acceleratingCount,
            MatureTrendCount = matureCount,
            InitializingMarketCount = allMarkets.Count(m => !m.IsActive && m.IsEligible),
            FailedOrUnavailableMarketCount = failedCount,
            CurrentTimeframe = "4H",
            ActiveExchangeProvider = _marketDataProvider.ProviderName,
            ScannerStatus = "Running",
            LastSuccessfulMarketDataUpdate = lastUpdate,
            ScannerUptime = null,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    public IEnumerable<MarketListItemDto> GetMarkets(
        int page, int pageSize, string? search, string? direction, string? condition, string? status,
        string? sortBy, string? sortDirection)
    {
        var universe = _universeManager.CurrentUniverse;
        var monitoringStates = _monitoringStateRepository.GetAllAsync(
            _marketDataProvider.ProviderName).GetAwaiter().GetResult();

        var stateBySymbol = monitoringStates
            .ToDictionary(s => s.CanonicalSymbol, StringComparer.OrdinalIgnoreCase);

        var query = universe.Markets.Select(m =>
        {
            var state = stateBySymbol.TryGetValue(m.CanonicalSymbol, out var s) ? s : null;
            return new MarketListItemDto
            {
                Symbol = m.CanonicalSymbol,
                ExchangeSymbol = m.NativeSymbol,
                Provider = m.Provider,
                MarketType = "USDTPerpetual",
                Timeframe = "4H",
                InitializationStatus = state?.IsInitialized == true ? "Initialized" : "Discovered",
                MonitoringStatus = state?.IsActive == true ? "Monitoring" : "NotMonitoring",
                TrendDirection = state?.TrendDirection.ToString(),
                TrendCondition = state?.TrendCondition.ToString(),
                LastEvaluatedCandle = state?.LastProcessedCandleTime,
                LastMarketDataUpdate = state?.LastReceivedDataTime,
                CurrentPrice = m.LastPrice > 0 ? m.LastPrice : null,
                Rank = m.Rank,
                QuoteVolume24h = m.QuoteVolume24h > 0 ? m.QuoteVolume24h : null
            };
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToUpperInvariant();
            query = query.Where(m =>
                m.Symbol.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                m.ExchangeSymbol.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(direction))
        {
            query = query.Where(m =>
                m.TrendDirection?.Equals(direction, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(condition))
        {
            query = query.Where(m =>
                m.TrendCondition?.Equals(condition, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(m =>
                m.MonitoringStatus.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        query = (sortBy?.ToLowerInvariant(), sortDirection?.ToLowerInvariant()) switch
        {
            ("symbol", "desc") => query.OrderByDescending(m => m.Symbol, StringComparer.OrdinalIgnoreCase),
            ("symbol", _) => query.OrderBy(m => m.Symbol, StringComparer.OrdinalIgnoreCase),
            ("price", "desc") => query.OrderByDescending(m => m.CurrentPrice),
            ("price", _) => query.OrderBy(m => m.CurrentPrice),
            ("rank", "desc") => query.OrderByDescending(m => m.Rank),
            ("rank", _) => query.OrderBy(m => m.Rank),
            ("volume", "desc") => query.OrderByDescending(m => m.QuoteVolume24h),
            ("volume", _) => query.OrderBy(m => m.QuoteVolume24h),
            _ => query.OrderBy(m => m.Symbol, StringComparer.OrdinalIgnoreCase)
        };

        return query.Skip((page - 1) * pageSize).Take(pageSize).ToList();
    }

    public int GetMarketsCount(string? search, string? direction, string? condition, string? status)
    {
        var universe = _universeManager.CurrentUniverse;
        var monitoringStates = _monitoringStateRepository.GetAllAsync(
            _marketDataProvider.ProviderName).GetAwaiter().GetResult();

        var stateBySymbol = monitoringStates
            .ToDictionary(s => s.CanonicalSymbol, StringComparer.OrdinalIgnoreCase);

        var query = universe.Markets.Select(m =>
        {
            var state = stateBySymbol.TryGetValue(m.CanonicalSymbol, out var s) ? s : null;
            return new
            {
                Symbol = m.CanonicalSymbol,
                ExchangeSymbol = m.NativeSymbol,
                TrendDirection = state?.TrendDirection.ToString(),
                TrendCondition = state?.TrendCondition.ToString(),
                MonitoringStatus = state?.IsActive == true ? "Monitoring" : "NotMonitoring"
            };
        });

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToUpperInvariant();
            query = query.Where(m =>
                m.Symbol.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                m.ExchangeSymbol.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(direction))
        {
            query = query.Where(m =>
                m.TrendDirection?.Equals(direction, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(condition))
        {
            query = query.Where(m =>
                m.TrendCondition?.Equals(condition, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(m =>
                m.MonitoringStatus.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        return query.Count();
    }

    public MarketDetailDto? GetMarketDetail(string symbol)
    {
        var universe = _universeManager.CurrentUniverse;
        var entry = universe.Markets.FirstOrDefault(m =>
            string.Equals(m.CanonicalSymbol, symbol, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return null;

        var monitoringStates = _monitoringStateRepository.GetAllAsync(
            _marketDataProvider.ProviderName).GetAwaiter().GetResult();

        var state = monitoringStates.FirstOrDefault(s =>
            string.Equals(s.CanonicalSymbol, symbol, StringComparison.OrdinalIgnoreCase));

        return new MarketDetailDto
        {
            Symbol = entry.CanonicalSymbol,
            ExchangeSymbol = entry.NativeSymbol,
            Provider = entry.Provider,
            MarketType = "USDTPerpetual",
            Timeframe = "4H",
            InitializationStatus = state?.IsInitialized == true ? "Initialized" : "Discovered",
            MonitoringStatus = state?.IsActive == true ? "Monitoring" : "NotMonitoring",
            IsInitialized = state?.IsInitialized ?? false,
            IsActive = entry.IsActive,
            Rank = entry.Rank,
            QuoteVolume24h = entry.QuoteVolume24h > 0 ? entry.QuoteVolume24h : null,
            LastPrice = entry.LastPrice > 0 ? entry.LastPrice : null,
            ContractStatus = string.IsNullOrWhiteSpace(entry.ContractStatus) ? null : entry.ContractStatus,
            DiscoveredAt = entry.DiscoveredAt,
            LastTickerUpdate = entry.LastTickerUpdate,
            LastProcessedCandleTime = state?.LastProcessedCandleTime,
            LastEvaluationTime = state?.LastEvaluationTime,
            LastReceivedDataTime = state?.LastReceivedDataTime,
            TrendDirection = state?.TrendDirection.ToString(),
            TrendCondition = state?.TrendCondition.ToString(),
            TrendEstablishedAt = state?.TrendEstablishedAt,
            ConfirmedRetestCount = state?.ConfirmedRetestCount ?? 0,
            BoundaryRetestCount = state?.ConfirmedRetestCount ?? 0,
            LastError = state?.LastError,
            ConsecutiveErrors = state?.ConsecutiveErrors ?? 0,
            LastEvaluatedCandle = state?.LastProcessedCandleTime
        };
    }

    public async Task<IReadOnlyList<CandleDto>> GetCandlesAsync(
        string symbol, string timeframe, DateTimeOffset? startTime, DateTimeOffset? endTime, int? limit)
    {
        var now = DateTimeOffset.UtcNow;
        var latestClosed = GetLatestClosedCandleStart(now, TimeSpan.FromHours(4));

        var request = MarketDataRequest.ForHistoricalWindow(
            symbol,
            Timeframe.Parse(timeframe ?? "4H"),
            startTime ?? latestClosed.AddDays(-14),
            endTime ?? latestClosed,
            limit);

        var candles = await _marketDataProvider.GetCandlesAsync(request);

        return candles.Select(c => new CandleDto
        {
            Symbol = c.Symbol,
            StartTime = c.StartTime,
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume,
            Turnover = c.Turnover
        }).ToList();
    }

    public async Task<IReadOnlyList<TrendHistoryItemDto>> GetTrendHistoryAsync(string symbol)
    {
        var monitoringStates = await _monitoringStateRepository.GetAllAsync(
            _marketDataProvider.ProviderName);

        var state = monitoringStates.FirstOrDefault(s =>
            string.Equals(s.CanonicalSymbol, symbol, StringComparison.OrdinalIgnoreCase));

        if (state is null)
            return [];

        return new[]
        {
            new TrendHistoryItemDto
            {
                Symbol = symbol,
                Timeframe = state.Timeframe,
                PreviousDirection = "Neutral",
                PreviousCondition = "NoValidTrend",
                NewDirection = state.TrendDirection.ToString(),
                NewCondition = state.TrendCondition.ToString(),
                Timestamp = state.LastEvaluationTime ?? state.LastProcessedCandleTime ?? DateTimeOffset.UtcNow,
                Reason = "Initial trend evaluation",
                UpperBoundaryPrice = null,
                LowerBoundaryPrice = null,
                Atr = null
            }
        };
    }

    public ScannerStatusDto GetScannerStatus()
    {
        var universe = _universeManager.CurrentUniverse;
        var monitoringStates = _monitoringStateRepository.GetAllAsync(
            _marketDataProvider.ProviderName).GetAwaiter().GetResult();

        var lastUpdate = monitoringStates
            .Where(s => s.LastReceivedDataTime.HasValue)
            .MaxBy(s => s.LastReceivedDataTime.Value)?
            .LastReceivedDataTime;

        var webSocketConnected = monitoringStates
            .Any(s => s.LastReceivedDataTime.HasValue &&
                      s.LastReceivedDataTime.Value > DateTimeOffset.UtcNow.AddMinutes(-10));

        return new ScannerStatusDto
        {
            OverallState = "Running",
            BackgroundWorkerState = "Running",
            ActiveProvider = _marketDataProvider.ProviderName,
            MonitoredMarketCount = monitoringStates.Count(s => s.IsActive),
            ActiveSubscriptions = monitoringStates.Count(s => s.IsActive),
            LastSuccessfulMarketDataUpdate = lastUpdate,
            WebSocketConnected = webSocketConnected,
            ReconnectionStatus = webSocketConnected ? "Connected" : "Disconnected",
            LastError = monitoringStates
                .Where(s => !string.IsNullOrWhiteSpace(s.LastError))
                .Select(s => s.LastError)
                .FirstOrDefault(),
            ScannerStartTime = null,
            Uptime = null
        };
    }

    private static DateTimeOffset GetLatestClosedCandleStart(DateTimeOffset now, TimeSpan interval)
    {
        var intervalMs = (long)interval.TotalMilliseconds;
        var alignedMs = (now.ToUnixTimeMilliseconds() / intervalMs) * intervalMs;
        return DateTimeOffset.FromUnixTimeMilliseconds(alignedMs) - interval;
    }
}