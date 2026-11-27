using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Events;
using TradingScanner.Infrastructure.GateIo;

namespace TradingScanner.Infrastructure.MarketScanning.Streaming;

public sealed class GateIoFuturesWebSocketStream : IMarketDataStream
{
    private readonly GateIoMarketDataProviderOptions _options;
    private readonly ISymbolNormalizer _symbolNormalizer;
    private readonly MarketMonitoringOptions _monitoringOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GateIoFuturesWebSocketStream> _logger;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _readCts;
    private readonly Channel<ClosedCandleEvent> _closedCandleChannel;
    private readonly ConcurrentDictionary<string, byte> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private int _reconnectAttempt;

    public GateIoFuturesWebSocketStream(
        GateIoMarketDataProviderOptions options,
        ISymbolNormalizer symbolNormalizer,
        MarketMonitoringOptions monitoringOptions,
        TimeProvider? timeProvider = null,
        ILogger<GateIoFuturesWebSocketStream>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _symbolNormalizer = symbolNormalizer ?? throw new ArgumentNullException(nameof(symbolNormalizer));
        _monitoringOptions = monitoringOptions ?? throw new ArgumentNullException(nameof(monitoringOptions));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<GateIoFuturesWebSocketStream>.Instance;

        _closedCandleChannel = Channel.CreateBounded<ClosedCandleEvent>(
            new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
                SingleReader = false
            });
    }

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                return;
            }

            _ws?.Dispose();
            _ws = new ClientWebSocket();

            // Gate.io USDT-settled futures WebSocket endpoint.
            var wsUrl = BuildWebSocketUrl();
            _logger.LogInformation("Connecting to Gate.io WebSocket: {Url}", wsUrl);

            await _ws.ConnectAsync(new Uri(wsUrl), cancellationToken);
            _reconnectAttempt = 0;

            _logger.LogInformation("Gate.io WebSocket connected successfully.");

            // Start the read loop.
            _readCts?.Cancel();
            _readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);
            _ = Task.Run(() => PingLoopAsync(_readCts.Token), _readCts.Token);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task SubscribeAsync(
        IReadOnlyList<string> nativeSymbols,
        string interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nativeSymbols);
        if (nativeSymbols.Count == 0) return;

        // Gate.io futures.candlesticks subscription payload:
        // { "time": <unix>, "channel": "futures.candlesticks", "event": "subscribe", "payload": ["<interval>", "<contract>"] }
        // We subscribe one symbol at a time to track subscriptions accurately.
        foreach (var symbol in nativeSymbols)
        {
            var key = $"{symbol}|{interval}";
            if (!_subscribedSymbols.TryAdd(key, 1))
            {
                continue; // Already subscribed.
            }

            var msg = new
            {
                time = _timeProvider.GetUtcNow().ToUnixTimeSeconds(),
                channel = "futures.candlesticks",
                @event = "subscribe",
                payload = new[] { interval.ToLowerInvariant(), symbol }
            };

            await SendJsonAsync(msg, cancellationToken);
            _logger.LogDebug("Subscribed to {Symbol} {Interval} candlesticks.", symbol, interval);
        }
    }

    public async Task UnsubscribeAsync(
        IReadOnlyList<string> nativeSymbols,
        string interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nativeSymbols);
        if (nativeSymbols.Count == 0) return;

        foreach (var symbol in nativeSymbols)
        {
            var key = $"{symbol}|{interval}";
            if (!_subscribedSymbols.TryRemove(key, out _))
            {
                continue;
            }

            var msg = new
            {
                time = _timeProvider.GetUtcNow().ToUnixTimeSeconds(),
                channel = "futures.candlesticks",
                @event = "unsubscribe",
                payload = new[] { interval.ToLowerInvariant(), symbol }
            };

            await SendJsonAsync(msg, cancellationToken);
            _logger.LogDebug("Unsubscribed from {Symbol} {Interval} candlesticks.", symbol, interval);
        }
    }

    public async IAsyncEnumerable<ClosedCandleEvent> ReadClosedCandlesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in _closedCandleChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _readCts?.Cancel();
        _closedCandleChannel.Writer.TryComplete();

        if (_ws is not null)
        {
            if (_ws.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None);
                }
                catch
                {
                    // Ignore close errors during disposal.
                }
            }

            _ws.Dispose();
        }

        _readCts?.Dispose();
    }

    // ── Internals ──

    private string BuildWebSocketUrl()
    {
        // Convert REST base URL to WebSocket URL.
        // https://api.gateio.ws -> wss://fx-ws.gateio.ws/v4/ws/usdt
        // We use the documented endpoint directly.
        var settlement = _options.Settlement.ToLowerInvariant();
        return $"wss://fx-ws.gateio.ws/v4/ws/{settlement}";
    }

    private async Task SendJsonAsync(object message, CancellationToken cancellationToken)
    {
        if (_ws?.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task PingLoopAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(_monitoringOptions.WebSocketPingIntervalSeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);
                if (_ws?.State == WebSocketState.Open)
                {
                    var ping = new
                    {
                        time = _timeProvider.GetUtcNow().ToUnixTimeSeconds(),
                        channel = "futures.ping"
                    };
                    await SendJsonAsync(ping, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ping loop encountered an error.");
            }
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuffer = new MemoryStream();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_ws?.State != WebSocketState.Open)
                {
                    await ReconnectAsync(cancellationToken);
                    continue;
                }

                WebSocketReceiveResult result;
                messageBuffer.SetLength(0);

                do
                {
                    result = await _ws.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogWarning("WebSocket received close frame.");
                        await ReconnectAsync(cancellationToken);
                        messageBuffer.SetLength(0);
                        break;
                    }

                    messageBuffer.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (messageBuffer.Length == 0)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                ProcessMessage(json);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "WebSocket read error. Reconnecting.");
                await ReconnectAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in WebSocket read loop.");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("channel", out var channelProp))
            {
                return;
            }

            var channel = channelProp.GetString();

            // Ignore pong, subscribe/unsubscribe ack messages.
            if (!string.Equals(channel, "futures.candlesticks", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!root.TryGetProperty("event", out var eventProp) ||
                !string.Equals(eventProp.GetString(), "update", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!root.TryGetProperty("result", out var resultArray) ||
                resultArray.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in resultArray.EnumerateArray())
            {
                ProcessCandlestickUpdate(item);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse WebSocket message.");
        }
    }

    private void ProcessCandlestickUpdate(JsonElement item)
    {
        // Gate.io futures.candlesticks update payload:
        // { "t": timestamp, "v": volume, "c": close, "h": high, "l": low, "o": open, "n": interval, "a": amount/sum, "w": true/false }
        // "w": true means the candle window is complete/closed.
        // "n" contains the subscription identifier like "4h_BTC_USDT".

        if (!item.TryGetProperty("w", out var wProp))
        {
            return;
        }

        // Only process closed candles (w == true).
        var isClosed = wProp.ValueKind == JsonValueKind.True ||
                       (wProp.ValueKind == JsonValueKind.String &&
                        string.Equals(wProp.GetString(), "true", StringComparison.OrdinalIgnoreCase));

        if (!isClosed)
        {
            return;
        }

        if (!item.TryGetProperty("n", out var nProp) || nProp.GetString() is not { } subscriptionKey)
        {
            return;
        }

        // Parse the subscription key: "4h_BTC_USDT" -> interval="4h", symbol="BTC_USDT".
        var underscoreIndex = subscriptionKey.IndexOf('_');
        if (underscoreIndex < 0)
        {
            return;
        }

        var interval = subscriptionKey[..underscoreIndex];
        var nativeSymbol = subscriptionKey[(underscoreIndex + 1)..];

        if (!item.TryGetProperty("t", out var tProp))
        {
            return;
        }

        var timestamp = tProp.GetInt64();
        var startTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);

        var open = GetDecimalProperty(item, "o");
        var high = GetDecimalProperty(item, "h");
        var low = GetDecimalProperty(item, "l");
        var close = GetDecimalProperty(item, "c");
        var volume = GetDecimalProperty(item, "v");
        var turnover = GetStringDecimalProperty(item, "a");

        if (open <= 0 || high <= 0 || low <= 0 || close <= 0)
        {
            _logger.LogWarning("Invalid OHLC values in WebSocket candle for {Symbol} at {Time}.", nativeSymbol, startTime);
            return;
        }

        var canonicalSymbol = _symbolNormalizer.ToCanonicalSymbol(nativeSymbol);

        var candle = new Candle(
            Symbol: canonicalSymbol,
            StartTime: startTime,
            Open: open,
            High: high,
            Low: low,
            Close: close,
            Volume: volume,
            Turnover: turnover);

        var evt = new ClosedCandleEvent(
            Provider: "Gate.io USDT Perpetual Futures",
            CanonicalSymbol: canonicalSymbol,
            NativeSymbol: nativeSymbol,
            Timeframe: interval.ToUpperInvariant(),
            CandleStartTime: startTime,
            Candle: candle);

        if (!_closedCandleChannel.Writer.TryWrite(evt))
        {
            _logger.LogWarning(
                "Closed candle channel is full. Dropping candle for {Symbol} at {Time}.",
                canonicalSymbol, startTime);
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        _reconnectAttempt++;
        var baseDelay = _monitoringOptions.WebSocketReconnectBaseDelayMs;
        var maxDelay = _monitoringOptions.WebSocketReconnectMaxDelayMs;
        var delayMs = Math.Min(baseDelay * (int)Math.Pow(2, _reconnectAttempt - 1), maxDelay);
        var jitter = Random.Shared.Next(0, delayMs / 4);
        var totalDelay = delayMs + jitter;

        _logger.LogInformation(
            "WebSocket reconnecting in {DelayMs}ms (attempt {Attempt}).",
            totalDelay, _reconnectAttempt);

        await Task.Delay(totalDelay, cancellationToken);

        try
        {
            _ws?.Dispose();
            _ws = new ClientWebSocket();

            var wsUrl = BuildWebSocketUrl();
            await _ws.ConnectAsync(new Uri(wsUrl), cancellationToken);
            _reconnectAttempt = 0;

            _logger.LogInformation("WebSocket reconnected successfully.");

            // Re-subscribe all existing subscriptions.
            foreach (var key in _subscribedSymbols.Keys)
            {
                var parts = key.Split('|');
                if (parts.Length == 2)
                {
                    var symbol = parts[0];
                    var interval = parts[1];
                    var msg = new
                    {
                        time = _timeProvider.GetUtcNow().ToUnixTimeSeconds(),
                        channel = "futures.candlesticks",
                        @event = "subscribe",
                        payload = new[] { interval.ToLowerInvariant(), symbol }
                    };
                    await SendJsonAsync(msg, cancellationToken);
                }
            }

            _logger.LogInformation("Re-subscribed to {Count} channels after reconnection.", _subscribedSymbols.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "WebSocket reconnection failed (attempt {Attempt}).", _reconnectAttempt);
        }
    }

    private static decimal GetDecimalProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return 0m;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(prop.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) => v,
            _ => 0m
        };
    }

    private static decimal GetStringDecimalProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return 0m;
        }

        var str = prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        return decimal.TryParse(str, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }
}
