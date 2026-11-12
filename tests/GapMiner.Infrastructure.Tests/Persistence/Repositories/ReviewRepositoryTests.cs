using GapMiner.Domain.Entities;
using GapMiner.Domain.Enums;
using GapMiner.Infrastructure.Persistence;
using GapMiner.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// Story 2.3 acceptance for <see cref="IReviewRepository"/>: one
/// Testcontainers-verified test per interface method, with the bulk-insert
/// idempotency contract (re-calling with the same rows inserts nothing — the
/// precondition for Story 3.3's embedding job) asserted directly. Runs against
/// its own migrated database on the shared pgvector/pgvector:pg16 container.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReviewRepositoryTests(PostgresFixture fixture)
{
    private const string DatabaseName = "gapminer_repositories_reviews";

    private static readonly DateTimeOffset BaseTime =
        new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BulkInsertIgnoreDuplicatesAsync_inserts_new_rows_and_returns_their_count()
    {
        var target = await AddTargetAsync("Bulk insert probe");

        var reviews = new[]
        {
            NewReview(target.Id, sourceReviewId: "src-1", postedAt: BaseTime),
            NewReview(target.Id, sourceReviewId: "src-2", postedAt: BaseTime.AddDays(1)),
            NewReview(target.Id, sourceReviewId: "src-3", postedAt: BaseTime.AddDays(2)),
        };

        await using (var context = await NewContextAsync())
        {
            var repository = new ReviewRepository(context);
            Assert.Equal(3, await repository.BulkInsertIgnoreDuplicatesAsync(reviews));
        }

        await using (var context = await NewContextAsync())
        {
            var repository = new ReviewRepository(context);
            var stored = await repository.GetByTargetIdAsync(target.Id);

            Assert.Equal(3, stored.Count);
            var first = Assert.Single(stored, review => review.SourceReviewId == "src-1");
            Assert.Equal(5, first.StarRating);
            Assert.Equal("bulk insert probe text", first.ReviewText);
            Assert.Equal("probe author", first.ReviewAuthor);
            Assert.Equal(BaseTime, first.DatePosted);

            // The bulk insert is the scrape path: embeddings always start NULL.
            Assert.All(stored, review => Assert.Null(review.Embedding));
        }
    }

    [Fact]
    public async Task BulkInsertIgnoreDuplicatesAsync_re_called_with_the_same_rows_inserts_nothing()
    {
        var target = await AddTargetAsync("Idempotency probe");
        var reviews = new[]
        {
            NewReview(target.Id, sourceReviewId: "dup-1"),
            NewReview(target.Id, sourceReviewId: "dup-2"),
        };

        await using var context = await NewContextAsync();
        var repository = new ReviewRepository(context);

        Assert.Equal(2, await repository.BulkInsertIgnoreDuplicatesAsync(reviews));

        // Re-scrape of the same listing: same (competitor_target_id, source_review_id)
        // pairs via ON CONFLICT DO NOTHING — zero rows, no exception, no duplicates.
        Assert.Equal(0, await repository.BulkInsertIgnoreDuplicatesAsync(reviews));

        var stored = await repository.GetByTargetIdAsync(target.Id);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task BulkInsertIgnoreDuplicatesAsync_with_mixed_rows_inserts_only_the_new_ones()
    {
        var target = await AddTargetAsync("Mixed batch probe");

        var firstBatch = new[]
        {
            NewReview(target.Id, sourceReviewId: "kept-1"),
            NewReview(target.Id, sourceReviewId: "kept-2"),
        };
        var secondBatch = new[]
        {
            // One duplicate of the first batch, two genuinely new rows: only the
            // two new rows insert, leaving 1 + 2 = 3 rows total for this target.
            NewReview(target.Id, sourceReviewId: "kept-2"),
            NewReview(target.Id, sourceReviewId: "fresh-1"),
            NewReview(target.Id, sourceReviewId: "fresh-2"),
        };

        await using var context = await NewContextAsync();
        var repository = new ReviewRepository(context);

        Assert.Equal(2, await repository.BulkInsertIgnoreDuplicatesAsync(firstBatch));
        Assert.Equal(2, await repository.BulkInsertIgnoreDuplicatesAsync(secondBatch));

        var stored = await repository.GetByTargetIdAsync(target.Id);
        Assert.Equal(
            new[] { "fresh-1", "fresh-2", "kept-1", "kept-2" },
            stored.Select(review => review.SourceReviewId).OrderBy(id => id));
    }

    [Fact]
    public async Task BulkInsertIgnoreDuplicatesAsync_with_duplicates_inside_one_batch_inserts_each_row_once()
    {
        var target = await AddTargetAsync("In-batch duplicate probe");

        // A scraper paginating an unstable marketplace sort can re-emit the same
        // review within one run: the same (competitor_target_id, source_review_id)
        // pair twice inside ONE statement. ON CONFLICT DO NOTHING must skip the
        // in-flight duplicate too — returning 2 (the unique rows), never throwing
        // a unique violation.
        var batch = new[]
        {
            NewReview(target.Id, sourceReviewId: "in-batch-dup"),
            NewReview(target.Id, sourceReviewId: "in-batch-dup"),
            NewReview(target.Id, sourceReviewId: "in-batch-unique"),
        };

        await using var context = await NewContextAsync();
        var repository = new ReviewRepository(context);

        Assert.Equal(2, await repository.BulkInsertIgnoreDuplicatesAsync(batch));
        Assert.Equal(0, await repository.BulkInsertIgnoreDuplicatesAsync(batch));

        var stored = await repository.GetByTargetIdAsync(target.Id);
        Assert.Equal(
            new[] { "in-batch-dup", "in-batch-unique" },
            stored.Select(review => review.SourceReviewId).OrderBy(id => id));
    }

    [Fact]
    public async Task BulkInsertIgnoreDuplicatesAsync_with_an_empty_list_returns_zero()
    {
        await using var context = await NewContextAsync();
        var repository = new ReviewRepository(context);

        Assert.Equal(0, await repository.BulkInsertIgnoreDuplicatesAsync(Array.Empty<Review>()));
    }

    [Fact]
    public async Task GetUnembeddedAsync_returns_only_the_targets_unembedded_reviews_oldest_first()
    {
        var target = await AddTargetAsync("Unembedded probe");
        var otherTarget = await AddTargetAsync("Unembedded other target");

        await using var context = await NewContextAsync();
        var reviewRepository = new ReviewRepository(context);

        var oldest = NewReview(target.Id, sourceReviewId: "old", postedAt: BaseTime);
        var newest = NewReview(target.Id, sourceReviewId: "new", postedAt: BaseTime.AddDays(2));
        var middle = NewReview(target.Id, sourceReviewId: "mid", postedAt: BaseTime.AddDays(1));

        await reviewRepository.BulkInsertIgnoreDuplicatesAsync([oldest, middle, newest]);

        // An already-embedded review of the same target must be skipped — the tracked
        // EF path is used because BulkInsert deliberately persists no embeddings.
        context.Add(new Review(
            Guid.NewGuid(),
            target.Id,
            starRating: 4,
            reviewText: "already embedded probe",
            reviewAuthor: "probe",
            datePosted: BaseTime.AddDays(3),
            sourceReviewId: "embedded",
            embedding: new float[3072]));
        await context.SaveChangesAsync();

        // ...and so must other targets' unembedded reviews.
        await reviewRepository.BulkInsertIgnoreDuplicatesAsync(
        [
            NewReview(otherTarget.Id, sourceReviewId: "other-target"),
        ]);

        var unembedded = await reviewRepository.GetUnembeddedAsync(target.Id, limit: 100);

        Assert.Equal(
            new[] { "old", "mid", "new" },
            unembedded.Select(review => review.SourceReviewId));
    }

    [Fact]
    public async Task GetUnembeddedAsync_honors_the_limit_with_the_oldest_batch_first()
    {
        var target = await AddTargetAsync("Limit probe");

        var reviews = new[]
        {
            NewReview(target.Id, sourceReviewId: "limit-3", postedAt: BaseTime.AddDays(2)),
            NewReview(target.Id, sourceReviewId: "limit-1", postedAt: BaseTime),
            NewReview(target.Id, sourceReviewId: "limit-2", postedAt: BaseTime.AddDays(1)),
        };

        await using var context = await NewContextAsync();
        var repository = new ReviewRepository(context);
        await repository.BulkInsertIgnoreDuplicatesAsync(reviews);

        var batch = await repository.GetUnembeddedAsync(target.Id, limit: 2);

        Assert.Equal(new[] { "limit-1", "limit-2" }, batch.Select(review => review.SourceReviewId));
    }

    [Fact]
    public async Task GetByTargetIdAsync_returns_only_that_targets_reviews_in_posted_order()
    {
        var target = await AddTargetAsync("Ordered fetch probe");
        var otherTarget = await AddTargetAsync("Ordered fetch other");

        await using var context = await NewContextAsync();
        var repository = new ReviewRepository(context);

        await repository.BulkInsertIgnoreDuplicatesAsync(
        [
            NewReview(target.Id, sourceReviewId: "day-2", postedAt: BaseTime.AddDays(1)),
            NewReview(target.Id, sourceReviewId: "day-1", postedAt: BaseTime),
            NewReview(target.Id, sourceReviewId: "day-3", postedAt: BaseTime.AddDays(2)),
            NewReview(otherTarget.Id, sourceReviewId: "other"),
        ]);

        var stored = await repository.GetByTargetIdAsync(target.Id);

        Assert.Equal(new[] { "day-1", "day-2", "day-3" }, stored.Select(review => review.SourceReviewId));
    }

    [Fact]
    public async Task BulkInsertIgnoreDuplicatesAsync_honors_a_canceled_token()
    {
        var target = await AddTargetAsync("Cancellation probe");

        await using var context = await NewContextAsync();
        var repository = new ReviewRepository(context);

        using var canceled = new CancellationTokenSource(TimeSpan.Zero);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.BulkInsertIgnoreDuplicatesAsync(
                [NewReview(target.Id, sourceReviewId: "canceled")],
                canceled.Token));
    }

    private async Task<CompetitorTarget> AddTargetAsync(string name)
    {
        var target = new CompetitorTarget(
            Guid.NewGuid(),
            name,
            MarketplaceKind.ShopifyAppStore,
            new Uri($"https://apps.shopify.com/{name.ToLowerInvariant().Replace(' ', '-')}"),
            DateTimeOffset.UtcNow);

        await using var context = await NewContextAsync();
        await new CompetitorTargetRepository(context).AddAsync(target);

        return target;
    }

    private async Task<GapMinerDbContext> NewContextAsync()
        => await RepositoryTestDatabase.CreateContextAsync(fixture, DatabaseName);

    private static Review NewReview(Guid competitorTargetId, string sourceReviewId, DateTimeOffset? postedAt = null) => new(
        Guid.NewGuid(),
        competitorTargetId,
        starRating: 5,
        reviewText: "bulk insert probe text",
        reviewAuthor: "probe author",
        datePosted: postedAt ?? DateTimeOffset.UtcNow,
        sourceReviewId: sourceReviewId);
}
