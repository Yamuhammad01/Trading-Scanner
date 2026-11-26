using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Domain.TrendDetection.Abstractions;

public interface ITrendDetectionLogger
{
    void LogEvaluation(TrendState previousState, TrendEvaluation evaluation);
    void LogInsufficientData(string provider, string symbol, string timeframe, DateTimeOffset candleTime, string reason);
    void LogDuplicateSkipped(string provider, string symbol, string timeframe, DateTimeOffset candleTime);
}

public sealed class NullTrendDetectionLogger : ITrendDetectionLogger
{
    public static NullTrendDetectionLogger Instance { get; } = new();

    public void LogEvaluation(TrendState previousState, TrendEvaluation evaluation) { }
    public void LogInsufficientData(string provider, string symbol, string timeframe, DateTimeOffset candleTime, string reason) { }
    public void LogDuplicateSkipped(string provider, string symbol, string timeframe, DateTimeOffset candleTime) { }
}
