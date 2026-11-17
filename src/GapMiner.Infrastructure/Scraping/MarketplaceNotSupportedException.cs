using GapMiner.Domain.Enums;

namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// Thrown when a marketplace cannot be scraped: the registry has no entry for
/// the <see cref="MarketplaceKind"/>, or the entry is marked
/// <see cref="MarketplaceActorEntryStatus.NeedsVerification"/> because its
/// actor could not be verified against the live Apify Store (plan T-2.2: no
/// silent failure — an unverified actor must fail the scrape loudly and be
/// escalated for human verification).
/// </summary>
public sealed class MarketplaceNotSupportedException : Exception
{
    /// <summary>
    /// Initializes the exception with the unsupported marketplace and the
    /// reason it cannot be resolved.
    /// </summary>
    /// <param name="marketplaceKind">The marketplace that cannot be scraped.</param>
    /// <param name="reason">Why the marketplace cannot be resolved.</param>
    public MarketplaceNotSupportedException(MarketplaceKind marketplaceKind, string reason)
        : base($"Marketplace {marketplaceKind} is not supported: {reason}")
    {
        MarketplaceKind = marketplaceKind;
    }

    /// <summary>Gets the marketplace that could not be resolved.</summary>
    public MarketplaceKind MarketplaceKind { get; }
}
