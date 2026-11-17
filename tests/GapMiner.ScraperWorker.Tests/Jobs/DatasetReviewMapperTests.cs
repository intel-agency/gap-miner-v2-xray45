using System.Text.Json;
using GapMiner.Domain.Entities;
using GapMiner.Domain.Enums;
using GapMiner.Infrastructure.Scraping;
using GapMiner.ScraperWorker.Jobs;
using Xunit;

namespace GapMiner.ScraperWorker.Tests.Jobs;

/// <summary>
/// Story 3.3's mapping acceptance: dataset items map to <see cref="Review"/>
/// entities through the registry's per-marketplace field names. Golden items
/// mirror each verified actor's documented output (its live Apify Store README
/// example, verified 2026-11-17), so a drifted actor schema fails these tests
/// exactly where it would fail the pipeline.
/// </summary>
public sealed class DatasetReviewMapperTests
{
    private static readonly DatasetReviewMapper Mapper = new();

    private static MarketplaceActorEntry Entry(MarketplaceKind kind)
        => MarketplaceActorRegistry.Default.Resolve(kind);

    /// <summary>Deserializes a dataset item the way the Apify client would.</summary>
    private static ApifyDatasetItem Item(string json)
        => JsonSerializer.Deserialize<ApifyDatasetItem>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static readonly Guid TargetId = Guid.NewGuid();

    [Fact]
    public void Maps_a_shopify_item_with_its_human_readable_date()
    {
        // Example shape from the actor's store README (powerai/shopify-app-reviews-scraper):
        // 'date' is human-readable, not ISO 8601 — the documented parse caveat.
        var item = Item(
            """
            {"appReviewUrl":"https://apps.shopify.com/judgeme/reviews","id":"review-1925676",
            "rating":5,"date":"October 21, 2025","reviewer":"Pet ID Tags","country":"Australia",
            "usage":"Almost 8 years using the app",
            "review":"Helped me set up the collection page to show product reviews."}
            """);

        var result = Mapper.Map(Entry(MarketplaceKind.ShopifyAppStore), new[] { item }, TargetId);

        var review = Assert.Single(result.Reviews);
        Assert.Equal(TargetId, review.CompetitorTargetId);
        Assert.Equal("review-1925676", review.SourceReviewId);
        Assert.Equal(5, review.StarRating);
        Assert.Equal("Pet ID Tags", review.ReviewAuthor);
        Assert.Equal("Helped me set up the collection page to show product reviews.", review.ReviewText);
        Assert.Equal(new DateTimeOffset(2025, 10, 21, 0, 0, 0, TimeSpan.Zero), review.DatePosted);
        Assert.Equal(0, result.SkippedItemCount);
    }

    [Fact]
    public void Maps_a_chrome_web_store_item_with_its_iso_date()
    {
        // Field names from automation-lab/chrome-web-store-reviews-scraper's
        // documented output table.
        var item = Item(
            """
            {"reviewId":"0BkkMFXvNpQlhjFFNUDBkTmxDakE","reviewBody":"Stopped working after the update",
            "rating":1,"reviewerName":"A. User","reviewDate":"2026-08-05T04:24:34.526228Z",
            "language":"en","isReply":false}
            """);

        var result = Mapper.Map(Entry(MarketplaceKind.ChromeWebStore), new[] { item }, TargetId);

        var review = Assert.Single(result.Reviews);
        Assert.Equal("0BkkMFXvNpQlhjFFNUDBkTmxDakE", review.SourceReviewId);
        Assert.Equal("Stopped working after the update", review.ReviewText);
        Assert.Equal(1, review.StarRating);
        Assert.Equal("A. User", review.ReviewAuthor);
        Assert.Equal(2026, review.DatePosted.Year);
        Assert.Equal(8, review.DatePosted.Month);
    }

    [Fact]
    public void Maps_an_apple_item_with_score_and_user_name()
    {
        // Field names from thewolves/appstore-reviews-scraper's documented
        // 12-field output (date ISO 8601, score 1-5).
        var item = Item(
            """
            {"id":"1234567890","text":"Crashes on launch every time","score":2,
            "userName":"iosuser81","date":"2026-09-14T10:00:00.000Z","version":"4.2.1"}
            """);

        var result = Mapper.Map(Entry(MarketplaceKind.AppleAppStore), new[] { item }, TargetId);

        var review = Assert.Single(result.Reviews);
        Assert.Equal("1234567890", review.SourceReviewId);
        Assert.Equal("Crashes on launch every time", review.ReviewText);
        Assert.Equal(2, review.StarRating);
        Assert.Equal("iosuser81", review.ReviewAuthor);
        Assert.Equal(2026, review.DatePosted.Year);
    }

    [Fact]
    public void Maps_a_g2_item_with_published_at()
    {
        // Field names from automation-lab/g2-scraper's documented review table
        // (starRating 1-5, publishedAt ISO 8601).
        var item = Item(
            """
            {"reviewId":"slack-review-1","reviewText":"Search is unreliable at scale",
            "starRating":3,"reviewerName":"Mid-Market User","publishedAt":"2026-07-02T08:15:30.000Z"}
            """);

        var result = Mapper.Map(Entry(MarketplaceKind.G2), new[] { item }, TargetId);

        var review = Assert.Single(result.Reviews);
        Assert.Equal("slack-review-1", review.SourceReviewId);
        Assert.Equal(3, review.StarRating);
        Assert.Equal("Mid-Market User", review.ReviewAuthor);
        Assert.Equal(2026, review.DatePosted.Year);
    }

    [Theory]
    [InlineData("""{"id":"r1","text":"ok","score":"2","userName":"u","date":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"reviewId":"r1","reviewText":"ok","starRating":"3","reviewerName":"u","publishedAt":"2026-01-01T00:00:00Z"}""")]
    public void Accepts_a_rating_written_as_a_numeric_string(string json)
    {
        var kind = json.Contains("starRating") ? MarketplaceKind.G2 : MarketplaceKind.AppleAppStore;

        var result = Mapper.Map(Entry(kind), new[] { Item(json) }, TargetId);

        Assert.Single(result.Reviews);
        Assert.Equal(0, result.SkippedItemCount);
    }

    [Fact]
    public void Preserves_a_numeric_source_review_id_as_its_raw_literal()
    {
        var item = Item("""{"id":9007199254740993,"text":"ok","score":1,"userName":"u","date":"2026-01-01T00:00:00Z"}""");

        var result = Mapper.Map(Entry(MarketplaceKind.AppleAppStore), new[] { item }, TargetId);

        // The raw literal survives intact — larger than int64 precision would matter here.
        Assert.Equal("9007199254740993", Assert.Single(result.Reviews).SourceReviewId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Skips_items_whose_rating_is_outside_the_review_range(int rating)
    {
        var item = Item($$"""{"id":"r1","text":"ok","score":{{rating}},"userName":"u","date":"2026-01-01T00:00:00Z"}""");

        var result = Mapper.Map(Entry(MarketplaceKind.AppleAppStore), new[] { item }, TargetId);

        Assert.Empty(result.Reviews);
        Assert.Equal(1, result.SkippedItemCount);
    }

    [Fact]
    public void Skips_items_with_a_fractional_rating()
    {
        var item = Item("""{"id":"r1","text":"ok","score":4.5,"userName":"u","date":"2026-01-01T00:00:00Z"}""");

        var result = Mapper.Map(Entry(MarketplaceKind.AppleAppStore), new[] { item }, TargetId);

        Assert.Empty(result.Reviews);
        Assert.Equal(1, result.SkippedItemCount);
    }

    [Theory]
    [InlineData("""{"text":"ok","score":2,"userName":"u","date":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"id":"r1","score":2,"userName":"u","date":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"id":"r1","text":"  ","score":2,"userName":"u","date":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"id":"r1","text":"ok","userName":"u","date":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"id":"r1","text":"ok","score":2,"userName":"u"}""")]
    [InlineData("""{"id":"r1","text":"ok","score":2,"userName":"u","date":"not a date at all"}""")]
    public void Skips_items_missing_or_malforming_a_required_field(string json)
    {
        var item = Item(json);

        var result = Mapper.Map(Entry(MarketplaceKind.AppleAppStore), new[] { item }, TargetId);

        Assert.Empty(result.Reviews);
        Assert.Equal(1, result.SkippedItemCount);
    }

    [Fact]
    public void Maps_a_missing_author_to_an_empty_name()
    {
        var item = Item("""{"id":"r1","text":"the body is the analysis input","score":1,"date":"2026-01-01T00:00:00Z"}""");

        var result = Mapper.Map(Entry(MarketplaceKind.AppleAppStore), new[] { item }, TargetId);

        Assert.Equal(string.Empty, Assert.Single(result.Reviews).ReviewAuthor);
    }

    [Fact]
    public void Maps_an_empty_fields_item_to_a_skip()
    {
        var result = Mapper.Map(Entry(MarketplaceKind.G2), new[] { new ApifyDatasetItem() }, TargetId);

        Assert.Empty(result.Reviews);
        Assert.Equal(1, result.SkippedItemCount);
    }

    [Fact]
    public void Counts_skips_across_a_mixed_batch()
    {
        var good = Item("""{"id":"r1","text":"ok","score":1,"userName":"u","date":"2026-01-01T00:00:00Z"}""");
        var bad = Item("""{"id":"r2","text":"ok","score":9,"userName":"u","date":"2026-01-01T00:00:00Z"}""");
        var alsoGood = Item("""{"id":"r3","text":"also ok","score":3,"userName":"u","date":"2026-01-02T00:00:00Z"}""");

        var result = Mapper.Map(Entry(MarketplaceKind.AppleAppStore), new[] { good, bad, alsoGood }, TargetId);

        Assert.Equal(2, result.Reviews.Count);
        Assert.Equal(1, result.SkippedItemCount);
        Assert.Equal(new[] { "r1", "r3" }, result.Reviews.Select(review => review.SourceReviewId).ToArray());
    }

    [Fact]
    public void Normalizes_an_offset_less_timestamp_to_utc()
    {
        var item = Item("""{"id":"r1","text":"ok","score":1,"userName":"u","date":"2026-03-04T05:06:07.123456"}""");

        var result = Mapper.Map(Entry(MarketplaceKind.AppleAppStore), new[] { item }, TargetId);

        var review = Assert.Single(result.Reviews);
        Assert.Equal(TimeSpan.Zero, review.DatePosted.Offset);
        Assert.Equal(
            new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero).AddMicroseconds(123456),
            review.DatePosted);
    }

    [Fact]
    public void Rejects_a_null_item_list()
    {
        Assert.Throws<ArgumentNullException>(() => Mapper.Map(Entry(MarketplaceKind.G2), null!, TargetId));
    }
}
