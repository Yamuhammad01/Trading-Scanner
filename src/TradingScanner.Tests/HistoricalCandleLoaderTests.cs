using TradingScanner.Domain.Entities;
using TradingScanner.Domain.MarketData;
using TradingScanner.Infrastructure.Services;
using Xunit;

namespace TradingScanner.Tests;

public sealed class HistoricalCandleLoaderTests
{
    [Theory]
    [InlineData("5m", "5m")]
    [InlineData("1h", "1h")]
    [InlineData("1HR", "1h")]
    [InlineData("4h", "4h")]
    [InlineData("4hr", "4h")]
    public void TimeframeParse_SupportedValues_ReturnsExpectedTimeframe(
        string input,
        string expectedName)
    {
        var timeframe = Timeframe.Parse(input);

        Assert.Equal(expectedName, timeframe.Name);
    }

    [Fact]
    public void TimeframeParse_UnsupportedValue_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => Timeframe.Parse("15m"));
    }

    [Fact]
    public async Task LoadRangeAsync_OneHourThirtyDays_PaginatesAndValidatesAllCandles()
    {
        var now = new DateTimeOffset(2026, 11, 25, 20, 0, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);

        var provider = new FakeMarketDataProvider();
        var loader = new HistoricalCandleLoader(
            provider,
            new CandleValidator(),
            new FixedTimeProvider(now));

        var history = await loader.LoadRangeAsync(
            "BTCUSDT",
            Timeframe.OneHour,
            start,
            end);

        Assert.Equal("BTCUSDT", history.Symbol);
        Assert.Equal("1H", history.Timeframe);
        Assert.Equal(start, history.OldestCandleTime);
        Assert.Equal(end, history.LatestCandleTime);
        Assert.Equal(721, history.Candles.Count);
        Assert.True(provider.Requests.Count >= 4);
        Assert.All(provider.Requests, r => Assert.Equal(Timeframe.OneHour, r.Timeframe));
    }

    [Fact]
    public async Task LoadRangeAsync_EndTimeInFuture_ClampsToLatestClosedCandle()
    {
        var now = new DateTimeOffset(2026, 10, 24, 1, 30, 0, TimeSpan.Zero);
        var start = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var futureEnd = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

        var provider = new FakeMarketDataProvider();
        var loader = new HistoricalCandleLoader(
            provider,
            new CandleValidator(),
            new FixedTimeProvider(now));

        var history = await loader.LoadRangeAsync(
            "BTCUSDT",
            Timeframe.OneHour,
            start,
            futureEnd);

        // At 01:30 UTC, the 01:00 candle is still open, so latest closed is 00:00.
        Assert.Equal(start, history.OldestCandleTime);
        Assert.Equal(start, history.LatestCandleTime);
        Assert.Single(history.Candles);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeMarketDataProvider : IMarketDataProvider
    {
        public string ProviderName => "Fake";
        public List<MarketDataRequest> Requests { get; } = [];

        public Task<IReadOnlyList<Candle>> GetCandlesAsync(
            MarketDataRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            var interval = request.Timeframe.Interval;
            var start = request.StartTime!.Value;
            var end = request.EndTime!.Value;

            var candles = new List<Candle>();
            for (var time = start; time <= end; time = time.Add(interval))
            {
                candles.Add(new Candle(
                    Symbol: request.Symbol,
                    StartTime: time,
                    Open: 50000m,
                    High: 50500m,
                    Low: 49500m,
                    Close: 50200m,
                    Volume: 100m,
                    Turnover: 5000000m));
            }

            return Task.FromResult<IReadOnlyList<Candle>>(candles);
        }
    }
}
