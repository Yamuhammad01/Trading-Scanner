using System.Text.Json.Serialization;
using TradingScanner.Infrastructure.GateIo;

namespace TradingScanner.Infrastructure.GateIo;

public sealed class GateIoCandlestickResponse
{
    [JsonPropertyName("t")]
    public long Timestamp { get; init; }

    [JsonPropertyName("o")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal Open { get; init; }

    [JsonPropertyName("h")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal High { get; init; }

    [JsonPropertyName("l")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal Low { get; init; }

    [JsonPropertyName("c")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal Close { get; init; }

    [JsonPropertyName("v")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal ContractVolume { get; init; }

    [JsonPropertyName("sum")]
    public string? Turnover { get; init; }
}
