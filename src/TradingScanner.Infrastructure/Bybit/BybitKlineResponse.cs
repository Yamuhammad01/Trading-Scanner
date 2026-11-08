using System.Text.Json.Serialization;

namespace TradingScanner.Infrastructure.Bybit;

public sealed class BybitKlineResponse
{
    [JsonPropertyName("retCode")]
    public int RetCode { get; set; }

    [JsonPropertyName("retMsg")]
    public string RetMsg { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public BybitKlineResult? Result { get; set; }
}

public sealed class BybitKlineResult
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("list")]
    public List<string[]> List { get; set; } = [];
}