using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;
using TradingScanner.Api.Dtos;
using TradingScanner.Api.Hubs;
using TradingScanner.Domain.MarketScanning.Events;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Api.Services;

public sealed class ScannerEventBroadcaster : BackgroundService
{
    private readonly IHubContext<ScannerHub> _hubContext;
    private readonly ILogger<ScannerEventBroadcaster> _logger;
    private readonly Channel<TrendUpdatedEvent> _eventChannel;
    private readonly IServiceScopeFactory _scopeFactory;

    public ScannerEventBroadcaster(
        IHubContext<ScannerHub> hubContext,
        ILogger<ScannerEventBroadcaster> logger,
        IServiceScopeFactory scopeFactory)
    {
        _hubContext = hubContext;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _eventChannel = Channel.CreateBounded<TrendUpdatedEvent>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    public ChannelWriter<TrendUpdatedEvent> Writer => _eventChannel.Writer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var evt in _eventChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await BroadcastTrendUpdateAsync(evt, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast trend update for {Symbol}.", evt.CanonicalSymbol);
            }
        }
    }

    private async Task BroadcastTrendUpdateAsync(TrendUpdatedEvent evt, CancellationToken cancellationToken)
    {
        var dto = new ScannerEventDto
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            EventType = "TrendUpdated",
            Category = "TrendDetection",
            Symbol = evt.CanonicalSymbol,
            Provider = evt.Provider,
            Timeframe = evt.Timeframe,
            Timestamp = evt.Timestamp,
            Severity = "Information",
            Description = $"Trend updated for {evt.CanonicalSymbol}: {evt.Direction}/{evt.Condition}",
            Details = new
            {
                PreviousDirection = evt.PreviousDirection.ToString(),
                PreviousCondition = evt.PreviousCondition.ToString(),
                NewDirection = evt.Direction.ToString(),
                NewCondition = evt.Condition.ToString(),
                UpperBoundaryPrice = evt.TrendResult.UpperBoundaryPrice,
                LowerBoundaryPrice = evt.TrendResult.LowerBoundaryPrice,
                Atr = evt.TrendResult.Atr,
                TrendAge = evt.TrendResult.TrendAge?.TotalSeconds
            }
        };

        await _hubContext.Clients.All.SendAsync("TrendUpdated", dto, cancellationToken);

        if (!string.IsNullOrWhiteSpace(evt.CanonicalSymbol))
        {
            await _hubContext.Clients.Group($"market:{evt.CanonicalSymbol}").SendAsync("MarketTrendUpdated", dto, cancellationToken);
        }

        _logger.LogDebug("Broadcasted TrendUpdated event for {Symbol} {Direction}/{Condition}",
            evt.CanonicalSymbol, evt.Direction, evt.Condition);
    }

    public async Task BroadcastScannerStatusChangedAsync(string status, CancellationToken cancellationToken = default)
    {
        var dto = new ScannerEventDto
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            EventType = "ScannerStatusChanged",
            Category = "System",
            Timestamp = DateTimeOffset.UtcNow,
            Severity = "Information",
            Description = $"Scanner status changed: {status}"
        };

        await _hubContext.Clients.All.SendAsync("ScannerStatusChanged", dto, cancellationToken);
    }

    public async Task BroadcastMarketDiscoveredAsync(
        string symbol, string nativeSymbol, int rank, decimal quoteVolume24h,
        CancellationToken cancellationToken = default)
    {
        var dto = new ScannerEventDto
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            EventType = "MarketDiscovered",
            Category = "MarketDiscovery",
            Symbol = symbol,
            Timestamp = DateTimeOffset.UtcNow,
            Severity = "Information",
            Description = $"Market discovered: {symbol}",
            Details = new { NativeSymbol = nativeSymbol, Rank = rank, QuoteVolume24h = quoteVolume24h }
        };

        await _hubContext.Clients.All.SendAsync("MarketDiscovered", dto, cancellationToken);
    }

    public async Task BroadcastMarketInitializedAsync(
        string symbol, string nativeSymbol, int loadedCandles,
        CancellationToken cancellationToken = default)
    {
        var dto = new ScannerEventDto
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            EventType = "MarketInitialized",
            Category = "MarketInitialization",
            Symbol = symbol,
            Timestamp = DateTimeOffset.UtcNow,
            Severity = "Information",
            Description = $"Market initialized: {symbol} ({loadedCandles} candles)",
            Details = new { NativeSymbol = nativeSymbol, LoadedCandles = loadedCandles }
        };

        await _hubContext.Clients.All.SendAsync("MarketInitialized", dto, cancellationToken);
    }
}