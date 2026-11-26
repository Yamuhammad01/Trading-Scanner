using TradingScanner.Domain.Entities;
using TradingScanner.Domain.TrendDetection.Abstractions;
using TradingScanner.Domain.TrendDetection.Configuration;
using TradingScanner.Domain.TrendDetection.RollingSlope.Models;

namespace TradingScanner.Domain.TrendDetection.RollingSlope;

public sealed class RollingSlopeTrendDetector : ITrendDetector
{
    private readonly RollingSlopeOptions _options;
    private readonly AtrCalculator _atrCalculator;
    private readonly ChannelBoundaryCalculator _channelCalculator;
    private readonly TrendStateManager _stateManager;
    private readonly ITrendStateRepository _stateRepository;
    private readonly ITrendDetectionLogger _logger;
    private readonly TimeProvider _timeProvider;

    public RollingSlopeTrendDetector(
        RollingSlopeOptions options,
        AtrCalculator atrCalculator,
        ChannelBoundaryCalculator channelCalculator,
        TrendStateManager stateManager,
        ITrendStateRepository stateRepository,
        ITrendDetectionLogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        RollingSlopeConfigurationValidator.Validate(options);

        _options = options;
        _atrCalculator = atrCalculator;
        _channelCalculator = channelCalculator;
        _stateManager = stateManager;
        _stateRepository = stateRepository;
        _logger = logger ?? NullTrendDetectionLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TrendResult> EvaluateAsync(
        string provider,
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> closedCandles,
        CancellationToken cancellationToken = default)
    {
        ValidateInputs(provider, symbol, timeframe, closedCandles);

        var sortedCandles = EnsureChronological(closedCandles, symbol);
        var evaluationIndex = sortedCandles.Count - 1;

        var evaluation = await EvaluateAtIndexAsync(
            provider,
            symbol,
            timeframe,
            sortedCandles,
            evaluationIndex,
            cancellationToken);

        return evaluation.Result;
    }

    public async Task<IReadOnlyList<TrendResult>> ReplayAsync(
        string provider,
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> closedCandles,
        CancellationToken cancellationToken = default)
    {
        ValidateInputs(provider, symbol, timeframe, closedCandles);

        var sortedCandles = EnsureChronological(closedCandles, symbol);
        var results = new List<TrendResult>(sortedCandles.Count);

        for (var i = 0; i < sortedCandles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var evaluation = await EvaluateAtIndexAsync(
                provider,
                symbol,
                timeframe,
                sortedCandles,
                i,
                cancellationToken);

            results.Add(evaluation.Result);
        }

        return results;
    }

    private async Task<TrendEvaluation> EvaluateAtIndexAsync(
        string provider,
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> sortedCandles,
        int evaluationIndex,
        CancellationToken cancellationToken)
    {
        var evaluationCandle = sortedCandles[evaluationIndex];
        var evaluatedAt = _timeProvider.GetUtcNow();

        var existingState = await _stateRepository.GetAsync(
            provider,
            symbol,
            timeframe,
            _options.ConfigurationId,
            cancellationToken);

        var state = existingState ?? TrendState.CreateInitial(
            provider,
            symbol,
            timeframe,
            _options.ConfigurationId);

        // Do not process the same closed candle more than once.
        if (state.LastProcessedCandleTime.HasValue &&
            evaluationCandle.StartTime <= state.LastProcessedCandleTime.Value)
        {
            _logger.LogDuplicateSkipped(provider, symbol, timeframe, evaluationCandle.StartTime);

            var skippedResult = CreateResultFromState(state, evaluatedAt, "Candle already processed.");
            return new TrendEvaluation(skippedResult, state, WasSkippedAsDuplicate: true);
        }

        // Check sufficient lookback window (84 prior closed candles).
        if (evaluationIndex < _options.LookbackCandles)
        {
            var reason = $"Insufficient historical candles: need {_options.LookbackCandles} prior closed candles, got {evaluationIndex}.";
            _logger.LogInsufficientData(provider, symbol, timeframe, evaluationCandle.StartTime, reason);

            var insufficientState = state with
            {
                Direction = TrendDirection.Neutral,
                Condition = TrendCondition.NoValidTrend,
                EstablishedAt = null,
                BoundaryRetestCount = 0,
                IsInRetestInteraction = false,
                LastProcessedCandleTime = evaluationCandle.StartTime,
                IsChannelValid = false
            };

            await _stateRepository.SaveAsync(insufficientState, cancellationToken);

            var insufficientResult = new TrendResult
            {
                Provider = provider,
                Symbol = symbol,
                Timeframe = timeframe,
                Direction = TrendDirection.Neutral,
                Condition = TrendCondition.NoValidTrend,
                DiagnosticReason = reason,
                EvaluatedAt = evaluatedAt,
                LastProcessedCandleTime = evaluationCandle.StartTime
            };

            return new TrendEvaluation(insufficientResult, insufficientState, IsInsufficientData: true);
        }

        // Calculate ATR strictly before the evaluation candle.
        var atr = _atrCalculator.CalculateWilderAtr(
            sortedCandles,
            evaluationIndex,
            _options.AtrPeriod);

        if (!atr.HasValue || atr.Value <= 0m)
        {
            var reason = "ATR is zero, negative, or unavailable.";
            _logger.LogInsufficientData(provider, symbol, timeframe, evaluationCandle.StartTime, reason);

            var invalidAtrState = state with
            {
                Direction = TrendDirection.Neutral,
                Condition = TrendCondition.NoValidTrend,
                EstablishedAt = null,
                BoundaryRetestCount = 0,
                IsInRetestInteraction = false,
                LastProcessedCandleTime = evaluationCandle.StartTime,
                IsChannelValid = false,
                LastAtr = null
            };

            await _stateRepository.SaveAsync(invalidAtrState, cancellationToken);

            var invalidAtrResult = new TrendResult
            {
                Provider = provider,
                Symbol = symbol,
                Timeframe = timeframe,
                Direction = TrendDirection.Neutral,
                Condition = TrendCondition.NoValidTrend,
                DiagnosticReason = reason,
                EvaluatedAt = evaluatedAt,
                LastProcessedCandleTime = evaluationCandle.StartTime
            };

            return new TrendEvaluation(invalidAtrResult, invalidAtrState, IsInsufficientData: true);
        }

        var minSlope = _options.MinimumSlopeAtrPerCandle!.Value;
        var channel = _channelCalculator.Calculate(
            sortedCandles,
            evaluationIndex,
            _options.LookbackCandles,
            atr.Value,
            minSlope);

        var evaluation = _stateManager.Transition(
            state,
            evaluationCandle,
            channel,
            _options,
            evaluatedAt);

        await _stateRepository.SaveAsync(evaluation.UpdatedState, cancellationToken);

        _logger.LogEvaluation(state, evaluation);

        return evaluation;
    }

    private static void ValidateInputs(
        string provider,
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> closedCandles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeframe);
        ArgumentNullException.ThrowIfNull(closedCandles);

        if (closedCandles.Count == 0)
        {
            throw new ArgumentException("At least one closed candle is required.", nameof(closedCandles));
        }
    }

    private static IReadOnlyList<Candle> EnsureChronological(
        IReadOnlyList<Candle> candles,
        string expectedSymbol)
    {
        var list = candles.OrderBy(c => c.StartTime).ToList();

        for (var i = 0; i < list.Count; i++)
        {
            var candle = list[i];

            if (!string.Equals(candle.Symbol, expectedSymbol, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Mixed symbols detected: expected '{expectedSymbol}', got '{candle.Symbol}'.");
            }

            if (i > 0 && candle.StartTime == list[i - 1].StartTime)
            {
                throw new InvalidOperationException(
                    $"Duplicate candle timestamp detected at {candle.StartTime:O}.");
            }
        }

        return list;
    }

    private static TrendResult CreateResultFromState(
        TrendState state,
        DateTimeOffset evaluatedAt,
        string reason)
    {
        var lastTime = state.LastProcessedCandleTime ?? evaluatedAt;
        return new TrendResult
        {
            Provider = state.Provider,
            Symbol = state.Symbol,
            Timeframe = state.Timeframe,
            Direction = state.Direction,
            Condition = state.Condition,
            UpperSlope = state.LastUpperSlope,
            LowerSlope = state.LastLowerSlope,
            UpperNormalizedSlope = state.LastUpperNormalizedSlope,
            LowerNormalizedSlope = state.LastLowerNormalizedSlope,
            UpperBoundaryPrice = state.LastUpperBoundaryPrice,
            LowerBoundaryPrice = state.LastLowerBoundaryPrice,
            Atr = state.LastAtr,
            EstablishedAt = state.EstablishedAt,
            BoundaryRetestCount = state.BoundaryRetestCount,
            TrendAge = state.EstablishedAt.HasValue ? lastTime - state.EstablishedAt.Value : null,
            DiagnosticReason = reason,
            EvaluatedAt = evaluatedAt,
            LastProcessedCandleTime = lastTime
        };
    }
}
