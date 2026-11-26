namespace TradingScanner.Domain.TrendDetection.Configuration;

public sealed class RollingSlopeOptions
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string Timeframe { get; set; } = string.Empty;

    public bool ClosedCandlesOnly { get; set; }
    public string CandleOrder { get; set; } = string.Empty;
    public int LookbackCandles { get; set; }
    public bool EvaluateNewCandleAgainstPreviousWindow { get; set; }

    public string RegressionMethod { get; set; } = string.Empty;
    public string PriceTransform { get; set; } = string.Empty;
    public string UpperBoundarySource { get; set; } = string.Empty;
    public string LowerBoundarySource { get; set; } = string.Empty;
    public int MinimumValidWindow { get; set; }

    public int AtrPeriod { get; set; }
    public string AtrMethod { get; set; } = string.Empty;
    public string AtrReference { get; set; } = string.Empty;

    public decimal? MinimumSlopeAtrPerCandle { get; set; }

    public decimal TouchToleranceAtr { get; set; }
    public bool BoundaryInteractionClosedCandleRequired { get; set; }

    public decimal? UpperBreakoutThresholdAtr { get; set; }

    public bool MaturityEnabled { get; set; }
    public int MinimumDurationDays { get; set; }
    public int MinimumConfirmedBoundaryRetests { get; set; }

    public string ConfigurationId =>
        string.IsNullOrWhiteSpace(Name) ? "rolling_slope_trend_detector" : Name;
}
