namespace TradingScanner.Domain.Entities;

public sealed record Candle(
    string Symbol,
    DateTimeOffset StartTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal Turnover
);