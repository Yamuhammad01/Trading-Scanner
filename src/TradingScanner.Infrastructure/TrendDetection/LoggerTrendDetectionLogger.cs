using Microsoft.Extensions.Logging;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.RollingSlope;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Infrastructure.TrendDetection;

public sealed class LoggerTrendDetectionLogger : ITrendDetectionLogger
{
    private readonly ILogger<RollingSlopeTrendDetector> _logger;

    public LoggerTrendDetectionLogger(ILogger<RollingSlopeTrendDetector> logger)
    {
        _logger = logger;
    }

    public void LogEvaluation(TrendState previousState, TrendEvaluation evaluation)
    {
        var r = evaluation.Result;

        if (r.SignificantOppositeBoundaryBreakout)
        {
            _logger.LogWarning(
                "Significant opposite-boundary breakout invalidated trend. Provider={Provider} Symbol={Symbol} Timeframe={Timeframe} CandleTime={CandleTime} Direction={Direction} Condition={Condition} UpperSlope={UpperSlope} LowerSlope={LowerSlope} UpperNormSlope={UpperNormSlope} LowerNormSlope={LowerNormSlope} UpperBoundary={UpperBoundary} LowerBoundary={LowerBoundary} Atr={Atr} BreakoutDistanceAtr={BreakoutDistanceAtr}",
                r.Provider, r.Symbol, r.Timeframe, r.LastProcessedCandleTime,
                r.Direction, r.Condition, r.UpperSlope, r.LowerSlope,
                r.UpperNormalizedSlope, r.LowerNormalizedSlope,
                r.UpperBoundaryPrice, r.LowerBoundaryPrice, r.Atr, r.BreakoutDistanceAtr);
            return;
        }

        if (r.Direction != TrendDirection.Neutral && previousState.Direction == TrendDirection.Neutral)
        {
            _logger.LogInformation(
                "Initial directional channel established. Provider={Provider} Symbol={Symbol} Timeframe={Timeframe} CandleTime={CandleTime} Direction={Direction} Condition={Condition} UpperSlope={UpperSlope} LowerSlope={LowerSlope} UpperNormSlope={UpperNormSlope} LowerNormSlope={LowerNormSlope} UpperBoundary={UpperBoundary} LowerBoundary={LowerBoundary} Atr={Atr} EstablishedAt={EstablishedAt}",
                r.Provider, r.Symbol, r.Timeframe, r.LastProcessedCandleTime,
                r.Direction, r.Condition, r.UpperSlope, r.LowerSlope,
                r.UpperNormalizedSlope, r.LowerNormalizedSlope,
                r.UpperBoundaryPrice, r.LowerBoundaryPrice, r.Atr, r.EstablishedAt);
            return;
        }

        if (r.Direction != previousState.Direction)
        {
            _logger.LogInformation(
                "Trend direction changed from {PreviousDirection} to {Direction}. Provider={Provider} Symbol={Symbol} Timeframe={Timeframe} CandleTime={CandleTime} Condition={Condition} UpperSlope={UpperSlope} LowerSlope={LowerSlope} UpperNormSlope={UpperNormSlope} LowerNormSlope={LowerNormSlope} UpperBoundary={UpperBoundary} LowerBoundary={LowerBoundary} Atr={Atr} EstablishedAt={EstablishedAt}",
                previousState.Direction, r.Direction, r.Provider, r.Symbol, r.Timeframe, r.LastProcessedCandleTime,
                r.Condition, r.UpperSlope, r.LowerSlope, r.UpperNormalizedSlope, r.LowerNormalizedSlope,
                r.UpperBoundaryPrice, r.LowerBoundaryPrice, r.Atr, r.EstablishedAt);
            return;
        }

        if (r.Condition == TrendCondition.Accelerating)
        {
            _logger.LogInformation(
                "Directional acceleration detected. Provider={Provider} Symbol={Symbol} Timeframe={Timeframe} CandleTime={CandleTime} Direction={Direction} Condition={Condition} LowerBroken={LowerBroken} UpperBroken={UpperBroken} UpperBoundary={UpperBoundary} LowerBoundary={LowerBoundary} Atr={Atr} EstablishedAt={EstablishedAt}",
                r.Provider, r.Symbol, r.Timeframe, r.LastProcessedCandleTime,
                r.Direction, r.Condition, r.LowerBoundaryBroken, r.UpperBoundaryBroken,
                r.UpperBoundaryPrice, r.LowerBoundaryPrice, r.Atr, r.EstablishedAt);
            return;
        }

        if (r.BoundaryRetestCount > previousState.BoundaryRetestCount)
        {
            _logger.LogInformation(
                "Confirmed independent boundary retest #{RetestCount}. Provider={Provider} Symbol={Symbol} Timeframe={Timeframe} CandleTime={CandleTime} Direction={Direction} Condition={Condition} UpperBoundary={UpperBoundary} LowerBoundary={LowerBoundary} Atr={Atr} EstablishedAt={EstablishedAt}",
                r.BoundaryRetestCount, r.Provider, r.Symbol, r.Timeframe, r.LastProcessedCandleTime,
                r.Direction, r.Condition, r.UpperBoundaryPrice, r.LowerBoundaryPrice, r.Atr, r.EstablishedAt);
        }

        if (r.Condition == TrendCondition.Mature && previousState.Condition != TrendCondition.Mature)
        {
            _logger.LogInformation(
                "Trend reached maturity. Provider={Provider} Symbol={Symbol} Timeframe={Timeframe} CandleTime={CandleTime} Direction={Direction} Condition={Condition} RetestCount={RetestCount} EstablishedAt={EstablishedAt}",
                r.Provider, r.Symbol, r.Timeframe, r.LastProcessedCandleTime,
                r.Direction, r.Condition, r.BoundaryRetestCount, r.EstablishedAt);
            return;
        }

        _logger.LogDebug(
            "Rolling slope evaluation: Provider={Provider} Symbol={Symbol} Timeframe={Timeframe} CandleTime={CandleTime} Direction={Direction} Condition={Condition} UpperNormSlope={UpperNormSlope} LowerNormSlope={LowerNormSlope} UpperBoundary={UpperBoundary} LowerBoundary={LowerBoundary} Atr={Atr} EstablishedAt={EstablishedAt}",
            r.Provider, r.Symbol, r.Timeframe, r.LastProcessedCandleTime,
            r.Direction, r.Condition, r.UpperNormalizedSlope, r.LowerNormalizedSlope,
            r.UpperBoundaryPrice, r.LowerBoundaryPrice, r.Atr, r.EstablishedAt);
    }

    public void LogInsufficientData(
        string provider,
        string symbol,
        string timeframe,
        DateTimeOffset candleTime,
        string reason)
    {
        _logger.LogWarning(
            "Insufficient historical data for trend evaluation. Provider={Provider} Symbol={Symbol} Timeframe={Timeframe} CandleTime={CandleTime} Reason={Reason}",
            provider, symbol, timeframe, candleTime, reason);
    }

    public void LogDuplicateSkipped(
        string provider,
        string symbol,
        string timeframe,
        DateTimeOffset candleTime)
    {
        _logger.LogDebug(
            "Skipping already-processed candle. Provider={Provider} Symbol={Symbol} Timeframe={Timeframe} CandleTime={CandleTime}",
            provider, symbol, timeframe, candleTime);
    }
}
