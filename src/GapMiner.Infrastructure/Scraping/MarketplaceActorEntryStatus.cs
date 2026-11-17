namespace GapMiner.Infrastructure.Scraping;

/// <summary>
/// The verification state of a <see cref="MarketplaceActorEntry"/> (plan T-2.2).
/// An entry is <see cref="Verified"/> only when its actor ID resolves on the
/// live Apify Store <c>and</c> its dataset field mapping is documented on the
/// actor's own store page. Anything less — an unconfirmed actor, an
/// undocumented output shape, a plan candidate that no longer resolves — is
/// <see cref="NeedsVerification"/> and throws at resolution time instead of
/// silently producing garbage scrapes.
/// </summary>
public enum MarketplaceActorEntryStatus
{
    /// <summary>
    /// The actor ID and the entry's field mapping were verified against the
    /// live Apify Store; the entry resolves normally.
    /// </summary>
    Verified = 0,

    /// <summary>
    /// The actor ID (or its dataset field mapping) could not be verified
    /// against the live Apify Store. Resolving the entry throws
    /// <see cref="MarketplaceNotSupportedException"/> — a human must verify
    /// and flip the status before the marketplace can be scraped.
    /// </summary>
    NeedsVerification = 1,
}
