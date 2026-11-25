using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingScanner.Domain.MarketData;
using TradingScanner.Infrastructure.Bybit;
using TradingScanner.Infrastructure.GateIo;

namespace TradingScanner.Tests;

public class CommonContractTests
{
    [Fact]
    public void BybitProviderImplementsIMarketDataProvider()
    {
        var provider = new BybitMarketDataProvider(
            new HttpClient(),
            new BybitMarketDataProviderOptions());

        Assert.IsAssignableFrom<IMarketDataProvider>(provider);
        Assert.Equal("Bybit", provider.ProviderName);
    }

    [Fact]
    public void GateIoProviderImplementsIMarketDataProvider()
    {
        var provider = new GateIoMarketDataProvider(
            new HttpClient(),
            new GateIoMarketDataProviderOptions(),
            new GateIoContractMetadataCache(60));

        Assert.IsAssignableFrom<IMarketDataProvider>(provider);
        Assert.Equal("Gate.io USDT Perpetual Futures", provider.ProviderName);
    }

    [Fact]
    public void BybitCanBeSelectedThroughConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Provider"] = "Bybit",
                ["Providers:Bybit:BaseUrl"] = "https://api.bybit.com",
                ["Providers:Bybit:Category"] = "linear",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMarketDataProviderFromConfiguration(configuration);

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IMarketDataProvider>();

        Assert.IsType<BybitMarketDataProvider>(provider);
        Assert.Equal("Bybit", provider.ProviderName);
    }

    [Fact]
    public void GateIoCanBeSelectedThroughConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Provider"] = "GateIo",
                ["Providers:GateIo:BaseUrl"] = "https://api.gateio.ws",
                ["Providers:GateIo:Settlement"] = "usdt",
                ["Providers:GateIo:ContractMetadataCacheDurationMinutes"] = "60",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddMarketDataProviderFromConfiguration(configuration);

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IMarketDataProvider>();

        Assert.IsType<GateIoMarketDataProvider>(provider);
        Assert.Equal("Gate.io USDT Perpetual Futures", provider.ProviderName);
    }

    [Fact]
    public void UnsupportedProviderFailsClearly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MarketData:Provider"] = "Binance",
            })
            .Build();

        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddMarketDataProviderFromConfiguration(configuration));

        Assert.Contains("Binance", ex.Message);
        Assert.Contains("Bybit", ex.Message);
        Assert.Contains("GateIo", ex.Message);
    }

    [Fact]
    public void MissingProviderConfigurationFailsClearly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddMarketDataProviderFromConfiguration(configuration));

        Assert.Contains("MarketData:Provider", ex.Message);
    }

    [Fact]
    public void MarketDataRequestSupportsAllRequiredFields()
    {
        var request = new MarketDataRequest
        {
            Symbol = "BTCUSDT",
            MarketType = MarketType.USDTPerpetual,
            Timeframe = Timeframe.FourHours,
            StartTime = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            EndTime = DateTimeOffset.Parse("2026-01-15T00:00:00Z"),
            Limit = 84,
        };

        Assert.Equal("BTCUSDT", request.Symbol);
        Assert.Equal(MarketType.USDTPerpetual, request.MarketType);
        Assert.Equal(Timeframe.FourHours, request.Timeframe);
        Assert.Equal(84, request.Limit);
    }

    [Fact]
    public void TimeframeValuesAreCorrect()
    {
        Assert.Equal("5m", Timeframe.FiveMinutes.Name);
        Assert.Equal(TimeSpan.FromMinutes(5), Timeframe.FiveMinutes.Interval);
        Assert.Equal("1h", Timeframe.OneHour.Name);
        Assert.Equal(TimeSpan.FromHours(1), Timeframe.OneHour.Interval);
        Assert.Equal("4h", Timeframe.FourHours.Name);
        Assert.Equal(TimeSpan.FromHours(4), Timeframe.FourHours.Interval);
    }

    [Fact]
    public void MarketDataRequestForHistoricalWindowSetsDefaults()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var end = DateTimeOffset.Parse("2026-01-15T00:00:00Z");

        var request = MarketDataRequest.ForHistoricalWindow("BTCUSDT", start, end);

        Assert.Equal("BTCUSDT", request.Symbol);
        Assert.Equal(MarketType.USDTPerpetual, request.MarketType);
        Assert.Equal(Timeframe.FourHours, request.Timeframe);
        Assert.Equal(start, request.StartTime);
        Assert.Equal(end, request.EndTime);
        Assert.Null(request.Limit);
    }
}
