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
        return ForHistoricalWindow(symbol, Timeframe.FourHours, startTime, endTime);
    }

    public static MarketDataRequest ForHistoricalWindow(
        string symbol,
        Timeframe timeframe,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(timeframe);

        return new MarketDataRequest
        {
            Symbol = symbol,
            MarketType = MarketType.USDTPerpetual,
            Timeframe = timeframe,
            StartTime = startTime,
            EndTime = endTime,
            Limit = limit,
        };
    }

    public override string ToString()
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"Symbol={Symbol}, MarketType={MarketType}, Timeframe={Timeframe}, " +
            $"StartTime={StartTime}, EndTime={EndTime}, Limit={Limit}");
    }
}
