using System.Text.Json.Serialization;

namespace TradingScanner.Infrastructure.GateIo;

public sealed class GateIoTickerResponse
{
    [JsonPropertyName("contract")]
    public string Contract { get; init; } = string.Empty;

    [JsonPropertyName("last")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal Last { get; init; }

    [JsonPropertyName("mark_price")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal MarkPrice { get; init; }

    [JsonPropertyName("volume_24h")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal Volume24h { get; init; }

    [JsonPropertyName("volume_24h_base")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal Volume24hBase { get; init; }

    [JsonPropertyName("volume_24h_quote")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal Volume24hQuote { get; init; }

    [JsonPropertyName("volume_24h_settle")]
    [JsonConverter(typeof(DecimalStringOrNumberConverter))]
    public decimal Volume24hSettle { get; init; }
}
