using System.Globalization;

namespace TradingScanner.Domain.MarketData;

public sealed class Timeframe
{
    private Timeframe(string name, TimeSpan interval)
    {
        Name = name;
        Interval = interval;
    }

    public string Name { get; }

    public TimeSpan Interval { get; }

    public static Timeframe FiveMinutes { get; } = new("5m", TimeSpan.FromMinutes(5));
    public static Timeframe OneHour { get; } = new("1h", TimeSpan.FromHours(1));
    public static Timeframe FourHours { get; } = new("4h", TimeSpan.FromHours(4));

    public override string ToString() => Name;

    public static bool operator ==(Timeframe? left, Timeframe? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Name == right.Name;
    }

    public static bool operator !=(Timeframe? left, Timeframe? right)
    {
        return !(left == right);
    }

    public override bool Equals(object? obj)
    {
        return obj is Timeframe other && Name == other.Name;
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode(StringComparison.Ordinal);
    }
}
