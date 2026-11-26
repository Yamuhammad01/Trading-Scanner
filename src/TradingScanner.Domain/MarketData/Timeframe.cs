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

    public static Timeframe Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Timeframe cannot be null or empty.", nameof(value));
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "5m" => FiveMinutes,
            "1h" or "1hr" => OneHour,
            "4h" or "4hr" => FourHours,
            _ => throw new NotSupportedException(
                $"Unsupported timeframe '{value}'. Supported timeframes: 5m, 1h, 4h.")
        };
    }

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
