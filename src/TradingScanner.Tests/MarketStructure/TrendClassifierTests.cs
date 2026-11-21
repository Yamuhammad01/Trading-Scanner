using TradingScanner.Domain.MarketStructure;
using TradingScanner.Domain.MarketStructure.Models;
using TradingScanner.Domain.SwingDetection.Models;

namespace TradingScanner.Tests.MarketStructure;

public class TrendClassifierTests
{
    private readonly MarketStructureDetector _detector = new();
    private readonly TrendClassifier _classifier = new();

    [Fact]
    public void Classify_ShouldReturnBullish_WhenThreeHHAndHLTransitionsExist()
    {
        var swings = new[]
        {
            CreateSwing(SwingPointType.SwingHigh, 100, 0),
            CreateSwing(SwingPointType.SwingLow, 80, 1),
            CreateSwing(SwingPointType.SwingHigh, 110, 2),
            CreateSwing(SwingPointType.SwingLow, 85, 3),
            CreateSwing(SwingPointType.SwingHigh, 120, 4),
            CreateSwing(SwingPointType.SwingLow, 90, 5),
            CreateSwing(SwingPointType.SwingHigh, 130, 6),
            CreateSwing(SwingPointType.SwingLow, 95, 7)
        };

        var structure = _detector.Detect(swings);

        var result = _classifier.Classify(structure);

        Assert.Equal(TrendDirection.Bullish, result.Direction);
        Assert.Equal(3, result.HigherHighTransitions);
        Assert.Equal(3, result.HigherLowTransitions);
    }

    [Fact]
    public void Classify_ShouldReturnBearish_WhenThreeLHAndLLTransitionsExist()
    {
        var swings = new[]
        {
            CreateSwing(SwingPointType.SwingHigh, 150, 0),
            CreateSwing(SwingPointType.SwingLow, 130, 1),
            CreateSwing(SwingPointType.SwingHigh, 140, 2),
            CreateSwing(SwingPointType.SwingLow, 120, 3),
            CreateSwing(SwingPointType.SwingHigh, 130, 4),
            CreateSwing(SwingPointType.SwingLow, 110, 5),
            CreateSwing(SwingPointType.SwingHigh, 120, 6),
            CreateSwing(SwingPointType.SwingLow, 100, 7)
        };

        var structure = _detector.Detect(swings);

        var result = _classifier.Classify(structure);

        Assert.Equal(TrendDirection.Bearish, result.Direction);
        Assert.Equal(3, result.LowerHighTransitions);
        Assert.Equal(3, result.LowerLowTransitions);
    }

    [Fact]
    public void Classify_ShouldReturnNeutral_WhenOnlyTwoTransitionsExist()
    {
        var swings = new[]
        {
            CreateSwing(SwingPointType.SwingHigh, 100, 0),
            CreateSwing(SwingPointType.SwingLow, 80, 1),
            CreateSwing(SwingPointType.SwingHigh, 110, 2),
            CreateSwing(SwingPointType.SwingLow, 85, 3),
            CreateSwing(SwingPointType.SwingHigh, 120, 4),
            CreateSwing(SwingPointType.SwingLow, 90, 5)
        };

        var structure = _detector.Detect(swings);

        var result = _classifier.Classify(structure);

        Assert.Equal(TrendDirection.Neutral, result.Direction);
    }

    [Fact]
    public void Classify_ShouldReturnNeutral_WhenOneTransitionBreaksThePattern()
    {
        var swings = new[]
        {
            CreateSwing(SwingPointType.SwingHigh, 100, 0),
            CreateSwing(SwingPointType.SwingLow, 80, 1),
            CreateSwing(SwingPointType.SwingHigh, 110, 2),
            CreateSwing(SwingPointType.SwingLow, 85, 3),
            CreateSwing(SwingPointType.SwingHigh, 120, 4),
            CreateSwing(SwingPointType.SwingLow, 82, 5),
            CreateSwing(SwingPointType.SwingHigh, 130, 6),
            CreateSwing(SwingPointType.SwingLow, 95, 7)
        };

        var structure = _detector.Detect(swings);

        var result = _classifier.Classify(structure);

        Assert.Equal(TrendDirection.Neutral, result.Direction);
    }

    private static SwingPoint CreateSwing(
        SwingPointType type,
        decimal price,
        int index)
    {
        var occurredAt = new DateTimeOffset(
            2026, 11, 1, 0, 0, 0, TimeSpan.Zero)
            .AddHours(index * 4);

        return new SwingPoint(
            Symbol: "BTCUSDT",
            Timeframe: "4H",
            Type: type,
            Price: price,
            OccurredAt: occurredAt,
            ConfirmedAt: occurredAt.AddHours(8)
        );
    }
}