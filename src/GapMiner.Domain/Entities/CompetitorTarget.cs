using GapMiner.Domain.Enums;

namespace GapMiner.Domain.Entities;

/// <summary>
/// A competitor product tracked on one marketplace. Immutable after creation,
/// apart from the operational <see cref="LastScrapedAt"/> stamp the scrape
/// pipeline advances after every completed scrape (Story 3.3, lifecycle step 9).
/// </summary>
public sealed class CompetitorTarget
{
    /// <summary>Gets the aggregate identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the competitor product name.</summary>
    public string Name { get; init; }

    /// <summary>Gets the marketplace the product is listed on.</summary>
    public MarketplaceKind MarketplaceKind { get; init; }

    /// <summary>Gets the marketplace listing URL.</summary>
    public Uri MarketplaceUrl { get; init; }

    /// <summary>Gets the UTC moment the target started being tracked.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the UTC moment the scrape pipeline last completed a scrape of this
    /// target, or <see langword="null"/> while it has never been scraped.
    /// </summary>
    public DateTimeOffset? LastScrapedAt { get; init; }

    public CompetitorTarget(
        Guid id,
        string name,
        MarketplaceKind marketplaceKind,
        Uri marketplaceUrl,
        DateTimeOffset createdAt,
        DateTimeOffset? lastScrapedAt = null)
    {
        Id = id;
        Name = name;
        MarketplaceKind = marketplaceKind;
        MarketplaceUrl = marketplaceUrl;
        CreatedAt = createdAt;
        LastScrapedAt = lastScrapedAt;
    }
}
