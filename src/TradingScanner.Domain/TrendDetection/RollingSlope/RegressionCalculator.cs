namespace TradingScanner.Domain.TrendDetection.RollingSlope;

public readonly record struct LinearRegressionResult(
    decimal Intercept,
    decimal Slope,
    decimal ProjectedValue);

public sealed class RegressionCalculator
{
    public LinearRegressionResult Calculate(ReadOnlySpan<decimal> values, int projectionIndex)
    {
        var count = values.Length;
        if (count < 2)
        {
            throw new ArgumentException(
                "At least 2 data points are required for linear regression.",
                nameof(values));
        }

        decimal sumY = 0m;
        for (var i = 0; i < count; i++)
        {
            sumY += values[i];
        }

        var n = (decimal)count;
        var meanX = (n - 1m) / 2m;
        var meanY = sumY / n;

        // Closed-form sum of squared deviations of x = 0, 1, ..., n-1: n * (n^2 - 1) / 12
        var sxx = n * ((n * n) - 1m) / 12m;

        decimal sxy = 0m;
        for (var i = 0; i < count; i++)
        {
            var dx = i - meanX;
            var dy = values[i] - meanY;
            sxy += dx * dy;
        }

        var slope = sxx == 0m ? 0m : sxy / sxx;
        var intercept = meanY - (slope * meanX);
        var projected = intercept + (slope * projectionIndex);

        return new LinearRegressionResult(intercept, slope, projected);
    }
}
