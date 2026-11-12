using GapMiner.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GapMiner.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core-backed <see cref="IReviewRepository"/> over <see cref="GapMinerDbContext"/>.
/// The bulk insert is a single set-based SQL statement (array <c>unnest</c> +
/// <c>ON CONFLICT DO NOTHING</c>) rather than a tracked <c>SaveChanges</c> loop, so a
/// re-scrape skips already-stored reviews atomically instead of failing the batch.
/// </summary>
public sealed class ReviewRepository(GapMinerDbContext context) : IReviewRepository
{
    /// <summary>
    /// One INSERT over the snake_case <c>review</c> table. The conflict target is the
    /// unique composite index created by InitialSchema — the same constraint EF's
    /// tracked path would violate, resolved here by ignoring the conflicting rows.
    /// The <c>embedding</c> column is deliberately absent: scraped reviews are never
    /// embedded yet, so inserted rows start <c>NULL</c> for the AI worker to fill.
    /// </summary>
    private const string BulkInsertSql = """
        INSERT INTO review (id, competitor_target_id, star_rating, review_text, review_author, date_posted, source_review_id)
        SELECT r.id, r.competitor_target_id, r.star_rating, r.review_text, r.review_author, r.date_posted, r.source_review_id
        FROM unnest(
                @ids::uuid[],
                @competitor_target_ids::uuid[],
                @star_ratings::int[],
                @review_texts::text[],
                @review_authors::text[],
                @date_posted::timestamptz[],
                @source_review_ids::text[])
            AS r (id, competitor_target_id, star_rating, review_text, review_author, date_posted, source_review_id)
        ON CONFLICT (competitor_target_id, source_review_id) DO NOTHING
        """;

    /// <inheritdoc />
    public async Task<int> BulkInsertIgnoreDuplicatesAsync(
        IReadOnlyList<Review> reviews,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviews);

        if (reviews.Count == 0)
        {
            return 0;
        }

        object[] parameters =
        [
            new NpgsqlParameter<Guid[]> { ParameterName = "ids", TypedValue = reviews.Select(review => review.Id).ToArray() },
            new NpgsqlParameter<Guid[]>
            {
                ParameterName = "competitor_target_ids",
                TypedValue = reviews.Select(review => review.CompetitorTargetId).ToArray(),
            },
            new NpgsqlParameter<int[]>
            {
                ParameterName = "star_ratings",
                TypedValue = reviews.Select(review => review.StarRating).ToArray(),
            },
            new NpgsqlParameter<string[]>
            {
                ParameterName = "review_texts",
                TypedValue = reviews.Select(review => review.ReviewText).ToArray(),
            },
            new NpgsqlParameter<string[]>
            {
                ParameterName = "review_authors",
                TypedValue = reviews.Select(review => review.ReviewAuthor).ToArray(),
            },
            new NpgsqlParameter<DateTimeOffset[]>
            {
                ParameterName = "date_posted",
                TypedValue = reviews.Select(review => review.DatePosted).ToArray(),
            },
            new NpgsqlParameter<string[]>
            {
                ParameterName = "source_review_ids",
                TypedValue = reviews.Select(review => review.SourceReviewId).ToArray(),
            },
        ];

        return await context.Database.ExecuteSqlRawAsync(BulkInsertSql, parameters, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Review>> GetUnembeddedAsync(
        Guid targetId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        return await context.Reviews
            .AsNoTracking()
            .Where(review => review.CompetitorTargetId == targetId && review.Embedding == null)
            .OrderBy(review => review.DatePosted)
            .ThenBy(review => review.SourceReviewId)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Review>> GetByTargetIdAsync(
        Guid targetId,
        CancellationToken cancellationToken = default)
        => await context.Reviews
            .AsNoTracking()
            .Where(review => review.CompetitorTargetId == targetId)
            .OrderBy(review => review.DatePosted)
            .ThenBy(review => review.SourceReviewId)
            .ToListAsync(cancellationToken);
}
