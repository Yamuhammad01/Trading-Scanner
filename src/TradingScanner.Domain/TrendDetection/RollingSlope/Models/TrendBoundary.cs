namespace TradingScanner.Domain.TrendDetection.RollingSlope.Models;

public sealed record TrendBoundary(
    decimal Intercept,
    decimal Slope,
    decimal ProjectedPrice,
    decimal? NormalizedSlope);
