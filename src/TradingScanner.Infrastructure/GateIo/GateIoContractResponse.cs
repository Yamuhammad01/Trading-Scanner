using System.Text.Json.Serialization;

namespace TradingScanner.Infrastructure.GateIo;

public sealed class GateIoContractResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("quanto_multiplier")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal QuantoMultiplier { get; init; }
}
