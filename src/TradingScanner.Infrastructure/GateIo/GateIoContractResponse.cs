using System.Text.Json.Serialization;

namespace TradingScanner.Infrastructure.GateIo;

public sealed class GateIoContractResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("quanto_multiplier")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal QuantoMultiplier { get; init; }

    [JsonPropertyName("in_delisting")]
    public bool InDelisting { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("expire_time")]
    public long? ExpireTime { get; init; }
}
