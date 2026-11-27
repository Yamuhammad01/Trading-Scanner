using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Domain.MarketScanning.Abstractions;

public interface IMarketEligibilityFilter
{
    bool IsEligible(
        MarketInstrument instrument,
        MarketTicker? ticker,
        out string? ineligibilityReason);
}
