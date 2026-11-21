using TradingScanner.Domain.MarketStructure;
using TradingScanner.Domain.MarketStructure.Models;
using TradingScanner.Domain.SwingDetection.Models;

namespace TradingScanner.Tests.MarketStructure;

public class MarketStructureDetectorTests
{
    private readonly MarketStructureDetector _detector = new();

    [Fact]
    public void Detect_ShouldClassifyBullishStructureCorrectly()
    {
        var swings = CreateBullishSwings();

        var result = _detector.Detect(swings);

        var expected = new[]
        {
            StructureLabel.Baseline,
            StructureLabel.Baseline,
            StructureLabel.HH,
            StructureLabel.HL,
            StructureLabel.HH,
            StructureLabel.HL,
            StructureLabel.HH,
            StructureLabel.HL
        };

        Assert.Equal(
            expected,
            result.Select(x => x.Label).ToArray());
    }

    [Fact]
    public void Detect_ShouldClassifyBearishStructureCorrectly()
    {
        var swings = CreateBearishSwings();

        var result = _detector.Detect(swings);

        var expected = new[]
        {
            StructureLabel.Baseline,
            StructureLabel.Baseline,
            StructureLabel.LH,
            StructureLabel.LL,
            StructureLabel.LH,
            StructureLabel.LL,
            StructureLabel.LH,
            StructureLabel.LL
        };

        Assert.Equal(
            expected,
            result.Select(x => x.Label).ToArray());
    }

    [Fact]
    public void Detect_ShouldRejectConsecutiveSwingHighs()
    {
        var swings = new[]
        {
            CreateSwing(SwingPointType.SwingHigh, 100, 0),
            CreateSwing(SwingPointType.SwingHigh, 110, 1)
        };

        Assert.Throws<InvalidOperationException>(
            () => _detector.Detect(swings));
    }

    [Fact]
    public void Detect_ShouldRejectEqualConsecutiveHighs()
    {
        var swings = new[]
        {
            CreateSwing(SwingPointType.SwingHigh, 100, 0),
            CreateSwing(SwingPointType.SwingLow, 80, 1),
            CreateSwing(SwingPointType.SwingHigh, 100, 2)
        };

        Assert.Throws<InvalidOperationException>(
            () => _detector.Detect(swings));
    }

    [Fact]
    public void Detect_ShouldRejectMixedSymbols()
    {
        var swings = new[]
        {
            CreateSwing(SwingPointType.SwingHigh, 100, 0, "BTCUSDT"),
            CreateSwing(SwingPointType.SwingLow, 80, 1, "ETHUSDT")
        };

        Assert.Throws<InvalidOperationException>(
            () => _detector.Detect(swings));
    }

    private static List<SwingPoint> CreateBullishSwings()
    {
        return new List<SwingPoint>
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
    }

    private static List<SwingPoint> CreateBearishSwings()
    {
        return new List<SwingPoint>
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
    }

    private static SwingPoint CreateSwing(
        SwingPointType type,
        decimal price,
        int index,
        string symbol = "BTCUSDT")
    {
        var occurredAt = new DateTimeOffset(
            2026, 11, 1, 0, 0, 0, TimeSpan.Zero)
            .AddHours(index * 4);

        return new SwingPoint(
            Symbol: symbol,
            Timeframe: "4H",
            Type: type,
            Price: price,
            OccurredAt: occurredAt,
            ConfirmedAt: occurredAt.AddHours(8)
        );
    }
}