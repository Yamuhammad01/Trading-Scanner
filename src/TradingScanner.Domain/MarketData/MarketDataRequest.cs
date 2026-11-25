using System.Globalization;

namespace TradingScanner.Domain.MarketData;

public sealed class MarketDataRequest
{
    public string Symbol { get; init; } = string.Empty;

    public MarketType MarketType { get; init; } = MarketType.USDTPerpetual;

    public Timeframe Timeframe { get; init; } = Timeframe.FourHours;

    public DateTimeOffset? StartTime { get; init; }

    public DateTimeOffset? EndTime { get; init; }

    public int? Limit { get; init; }

    public static MarketDataRequest ForHistoricalWindow(
        string symbol,
        DateTimeOffset startTime,
        DateTimeOffset endTime)
    {
        return new MarketDataRequest
        {
            Symbol = symbol,
            MarketType = MarketType.USDTPerpetual,
            Timeframe = Timeframe.FourHours,
            StartTime = startTime,
            EndTime = endTime,
        };
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"Symbol={Symbol}, MarketType={MarketType}, Timeframe={Timeframe}, " +
            $"StartTime={StartTime}, EndTime={EndTime}, Limit={Limit}");
    }
}
