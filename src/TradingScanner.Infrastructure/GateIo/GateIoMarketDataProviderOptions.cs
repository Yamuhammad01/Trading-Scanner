namespace TradingScanner.Infrastructure.GateIo;

public sealed class GateIoMarketDataProviderOptions
{
    public string BaseUrl { get; init; } = "https://api.gateio.ws";

    public string Settlement { get; init; } = "usdt";

    public int ContractMetadataCacheDurationMinutes { get; init; } = 60;
}
