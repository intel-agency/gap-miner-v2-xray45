using GapMiner.Domain.Enums;

namespace GapMiner.Domain.Entities;

/// <summary>
/// A competitor product tracked on one marketplace. Immutable after creation.
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

    public CompetitorTarget(
        Guid id,
        string name,
        MarketplaceKind marketplaceKind,
        Uri marketplaceUrl,
        DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        MarketplaceKind = marketplaceKind;
        MarketplaceUrl = marketplaceUrl;
        CreatedAt = createdAt;
    }
}
