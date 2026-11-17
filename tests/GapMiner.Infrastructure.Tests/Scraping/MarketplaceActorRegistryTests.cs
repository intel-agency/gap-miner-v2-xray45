using GapMiner.Domain.Enums;
using GapMiner.Infrastructure.Scraping;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Scraping;

/// <summary>
/// Story 3.2 acceptance for the marketplace actor registry: the default
/// registry resolves a live-store-verified entry (actor ID + complete dataset
/// field mapping) for every <see cref="MarketplaceKind"/>, entries marked
/// <see cref="MarketplaceActorEntryStatus.NeedsVerification"/> throw
/// <see cref="MarketplaceNotSupportedException"/> on resolution instead of
/// silently failing, and lookups for absent marketplaces fail the same way.
/// Actor IDs and field names below were verified against the live Apify Store
/// on 2026-11-17 (each actor's store page: documented dataset output).
/// </summary>
public sealed class MarketplaceActorRegistryTests
{
    [Fact]
    public void Default_registry_maps_every_MarketplaceKind()
    {
        foreach (MarketplaceKind kind in Enum.GetValues<MarketplaceKind>())
        {
            Assert.True(MarketplaceActorRegistry.Default.Entries.ContainsKey(kind),
                $"no registry entry for {kind}");
        }
    }

    [Fact]
    public void Default_entries_are_all_live_store_verified()
    {
        foreach (var entry in MarketplaceActorRegistry.Default.Entries.Values)
        {
            Assert.Equal(MarketplaceActorEntryStatus.Verified, entry.Status);
            Assert.False(string.IsNullOrWhiteSpace(entry.VerificationNote),
                $"{entry.MarketplaceKind} must cite its live-store verification");
        }
    }

    public static TheoryData<MarketplaceKind, string, string, string, string, string, string> VerifiedMappings => new()
    {
        // kind, actorId, sourceReviewId, review, starRating, author, date —
        // field names exactly as documented on each actor's live store page.
        { MarketplaceKind.ShopifyAppStore, "powerai~shopify-app-reviews-scraper", "id", "review", "rating", "reviewer", "date" },
        { MarketplaceKind.ChromeWebStore, "automation-lab~chrome-web-store-reviews-scraper", "reviewId", "reviewBody", "rating", "reviewerName", "reviewDate" },
        { MarketplaceKind.AppleAppStore, "thewolves~appstore-reviews-scraper", "id", "text", "score", "userName", "date" },
        { MarketplaceKind.G2, "automation-lab~g2-scraper", "reviewId", "reviewText", "starRating", "reviewerName", "publishedAt" },
    };

    [Theory]
    [MemberData(nameof(VerifiedMappings))]
    public void Resolve_returns_the_documented_mapping_per_marketplace(
        MarketplaceKind kind,
        string actorId,
        string sourceReviewIdFieldName,
        string reviewFieldName,
        string starRatingFieldName,
        string authorFieldName,
        string dateFieldName)
    {
        var entry = MarketplaceActorRegistry.Default.Resolve(kind);

        Assert.Equal(kind, entry.MarketplaceKind);
        Assert.Equal(actorId, entry.ActorId);
        Assert.Equal(sourceReviewIdFieldName, entry.SourceReviewIdFieldName);
        Assert.Equal(reviewFieldName, entry.ReviewFieldName);
        Assert.Equal(starRatingFieldName, entry.StarRatingFieldName);
        Assert.Equal(authorFieldName, entry.AuthorFieldName);
        Assert.Equal(dateFieldName, entry.DateFieldName);
    }

    [Fact]
    public void ActorIds_use_the_API_username_actor_form()
    {
        // /v2/acts/{actorId}/… takes a single path segment — a store-URL-style
        // "user/actor" ID would break routing. The tilde form is the documented one.
        foreach (var entry in MarketplaceActorRegistry.Default.Entries.Values)
        {
            Assert.DoesNotContain('/', entry.ActorId);
            Assert.Equal(2, entry.ActorId.Split('~').Length);
            Assert.All(entry.ActorId.Split('~'), part => Assert.False(string.IsNullOrWhiteSpace(part)));
        }
    }

    [Fact]
    public void Resolve_throws_for_NeedsVerification_entry()
    {
        var entry = new MarketplaceActorEntry(
            MarketplaceKind.G2,
            "vendor~unverified-g2-actor",
            "reviewId",
            "reviewText",
            "starRating",
            "reviewerName",
            "publishedAt",
            MarketplaceActorEntryStatus.NeedsVerification);
        var registry = new MarketplaceActorRegistry(
            new Dictionary<MarketplaceKind, MarketplaceActorEntry> { [MarketplaceKind.G2] = entry });

        var ex = Assert.Throws<MarketplaceNotSupportedException>(
            () => registry.Resolve(MarketplaceKind.G2));

        Assert.Equal(MarketplaceKind.G2, ex.MarketplaceKind);
        Assert.Contains("G2", ex.Message);
        Assert.Contains("vendor~unverified-g2-actor", ex.Message);
        Assert.Contains("NeedsVerification", ex.Message);
    }

    [Fact]
    public void Resolve_throws_for_marketplace_without_entry()
    {
        var registry = new MarketplaceActorRegistry(
            new Dictionary<MarketplaceKind, MarketplaceActorEntry>
            {
                [MarketplaceKind.AppleAppStore] = new(
                    MarketplaceKind.AppleAppStore,
                    "thewolves~appstore-reviews-scraper",
                    "id",
                    "text",
                    "score",
                    "userName",
                    "date",
                    MarketplaceActorEntryStatus.Verified),
            });

        // The registry owns only the Apple mapping; G2 has no entry here.
        var ex = Assert.Throws<MarketplaceNotSupportedException>(
            () => registry.Resolve(MarketplaceKind.G2));

        Assert.Equal(MarketplaceKind.G2, ex.MarketplaceKind);
        Assert.Contains("no entry", ex.Message);
    }

    [Fact]
    public void TryGet_returns_entry_without_throwing_for_NeedsVerification()
    {
        var entry = new MarketplaceActorEntry(
            MarketplaceKind.ShopifyAppStore,
            "vendor~unverified-shopify-actor",
            "id",
            "review",
            "rating",
            "reviewer",
            "date",
            MarketplaceActorEntryStatus.NeedsVerification);
        var registry = new MarketplaceActorRegistry(
            new Dictionary<MarketplaceKind, MarketplaceActorEntry> { [MarketplaceKind.ShopifyAppStore] = entry });

        var found = registry.TryGet(MarketplaceKind.ShopifyAppStore, out var got);

        Assert.True(found);
        Assert.Same(entry, got);
        Assert.Equal(MarketplaceActorEntryStatus.NeedsVerification, got!.Status);
    }

    [Fact]
    public void TryGet_returns_false_for_missing_marketplace()
    {
        var found = MarketplaceActorRegistry.Default.TryGet((MarketplaceKind)99, out var got);

        Assert.False(found);
        Assert.Null(got);
    }

    [Fact]
    public void Constructor_rejects_null_entries()
    {
        Assert.Throws<ArgumentNullException>(
            () => new MarketplaceActorRegistry(null!));
    }

    [Fact]
    public void Constructor_rejects_empty_entries()
    {
        Assert.Throws<ArgumentException>(
            () => new MarketplaceActorRegistry(new Dictionary<MarketplaceKind, MarketplaceActorEntry>()));
    }

    public static TheoryData<string> BlankMappingFields => new()
    {
        "ActorId",
        "SourceReviewIdFieldName",
        "ReviewFieldName",
        "StarRatingFieldName",
        "AuthorFieldName",
        "DateFieldName",
    };

    [Theory]
    [MemberData(nameof(BlankMappingFields))]
    public void Constructor_rejects_entries_with_blank_mapping_fields(string blankField)
    {
        var entry = new MarketplaceActorEntry(
            MarketplaceKind.AppleAppStore,
            blankField == "ActorId" ? " " : "thewolves~appstore-reviews-scraper",
            blankField == "SourceReviewIdFieldName" ? "" : "id",
            blankField == "ReviewFieldName" ? " " : "text",
            blankField == "StarRatingFieldName" ? "" : "score",
            blankField == "AuthorFieldName" ? " " : "userName",
            blankField == "DateFieldName" ? "" : "date",
            MarketplaceActorEntryStatus.Verified);

        var ex = Assert.Throws<ArgumentException>(
            () => new MarketplaceActorRegistry(
                new Dictionary<MarketplaceKind, MarketplaceActorEntry> { [MarketplaceKind.AppleAppStore] = entry }));

        Assert.Contains(blankField, ex.Message);
    }

    [Fact]
    public void Constructor_rejects_key_that_does_not_match_the_entry()
    {
        var entry = new MarketplaceActorEntry(
            MarketplaceKind.AppleAppStore,
            "thewolves~appstore-reviews-scraper",
            "id",
            "text",
            "score",
            "userName",
            "date",
            MarketplaceActorEntryStatus.Verified);

        Assert.Throws<ArgumentException>(
            () => new MarketplaceActorRegistry(
                new Dictionary<MarketplaceKind, MarketplaceActorEntry> { [MarketplaceKind.G2] = entry }));
    }

    [Fact]
    public void Constructor_rejects_undefined_status()
    {
        var entry = new MarketplaceActorEntry(
            MarketplaceKind.G2,
            "automation-lab~g2-scraper",
            "reviewId",
            "reviewText",
            "starRating",
            "reviewerName",
            "publishedAt",
            (MarketplaceActorEntryStatus)99);

        Assert.Throws<ArgumentException>(
            () => new MarketplaceActorRegistry(
                new Dictionary<MarketplaceKind, MarketplaceActorEntry> { [MarketplaceKind.G2] = entry }));
    }
}
