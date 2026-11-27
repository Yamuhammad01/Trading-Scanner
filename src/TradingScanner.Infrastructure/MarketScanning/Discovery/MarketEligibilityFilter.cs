using TradingScanner.Domain.MarketScanning.Abstractions;
using TradingScanner.Domain.MarketScanning.Configuration;
using TradingScanner.Domain.MarketScanning.Models;

namespace TradingScanner.Infrastructure.MarketScanning.Discovery;

public sealed class MarketEligibilityFilter : IMarketEligibilityFilter
{
    private readonly MarketUniverseOptions _options;

    public MarketEligibilityFilter(MarketUniverseOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsEligible(
        MarketInstrument instrument,
        MarketTicker? ticker,
        out string? ineligibilityReason)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        if (!string.Equals(
                instrument.SettlementCurrency,
                _options.RequiredSettlementCurrency,
                StringComparison.OrdinalIgnoreCase))
        {
            ineligibilityReason = $"Settlement currency '{instrument.SettlementCurrency}' != '{_options.RequiredSettlementCurrency}'.";
            return false;
        }

        if (!instrument.IsPerpetual)
        {
            ineligibilityReason = "Contract is not perpetual.";
            return false;
        }

        if (_options.ExcludeExpiredContracts && instrument.ExpiryTime.HasValue && instrument.ExpiryTime.Value <= instrument.DiscoveredAt)
        {
            ineligibilityReason = "Contract is expired.";
            return false;
        }

        if (_options.ExcludeInactiveContracts && (!instrument.IsActive || instrument.InDelisting))
        {
            ineligibilityReason = instrument.InDelisting ? "Contract is in delisting." : $"Contract status is '{instrument.Status}'.";
            return false;
        }

        if (instrument.QuantoMultiplier <= 0m)
        {
            ineligibilityReason = "Invalid quanto multiplier.";
            return false;
        }

        if (ticker is null)
        {
            ineligibilityReason = "Missing market ticker data.";
            return false;
        }

        if (ticker.LastPrice <= 0m || ticker.QuoteVolume24h < 0m)
        {
            ineligibilityReason = "Invalid market ticker price or volume.";
            return false;
        }

        if (ticker.QuoteVolume24h < _options.Minimum24HourQuoteVolume)
        {
            ineligibilityReason = $"24h quote volume ({ticker.QuoteVolume24h:N2}) is below minimum ({_options.Minimum24HourQuoteVolume:N2}).";
            return false;
        }

        ineligibilityReason = null;
        return true;
    }
}
