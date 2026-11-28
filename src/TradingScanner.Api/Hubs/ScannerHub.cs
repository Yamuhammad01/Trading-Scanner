using Microsoft.AspNetCore.SignalR;
using TradingScanner.Api.Dtos;
using TradingScanner.Domain.MarketScanning.Events;

namespace TradingScanner.Api.Hubs;

public sealed class ScannerHub : Hub
{
    private static int _connectionCount = 0;

    public override async Task OnConnectedAsync()
    {
        System.Threading.Interlocked.Increment(ref _connectionCount);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        System.Threading.Interlocked.Decrement(ref _connectionCount);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToMarket(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"market:{symbol}");
    }

    public async Task UnsubscribeFromMarket(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"market:{symbol}");
    }

    public static int ConnectionCount => _connectionCount;
}