using GapMiner.Domain.Entities;

namespace GapMiner.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence contract for <see cref="Review"/> rows
/// (plan T-1.3), including the duplicate-ignoring bulk insert that makes
/// re-scrapes idempotent and the unembedded batch feed for the AI worker.
/// Every asynchronous method propagates a <see cref="CancellationToken"/>.
/// </summary>
public interface IReviewRepository
{
    /// <summary>
    /// Bulk-inserts scraped reviews, silently skipping rows whose
    /// <see cref="Review.CompetitorTargetId"/> + <see cref="Review.SourceReviewId"/>
    /// already exist (SQL <c>ON CONFLICT DO NOTHING</c> against the unique composite
    /// index <c>ux_review_competitor_target_id_source_review_id</c>). Re-scraping a
    /// listing therefore never duplicates rows — the idempotency precondition for
    /// the embedding job (Story 3.3).
    /// </summary>
    /// <param name="reviews">
    /// The reviews to insert. <see cref="Review.Embedding"/> is not persisted by this
    /// method (scraped reviews arrive unembedded; the AI worker embeds them later), so
    /// every inserted row starts with a <see langword="null"/> embedding.
    /// </param>
    /// <param name="cancellationToken">Propagates notification that the operation should be canceled.</param>
    /// <returns>The number of rows actually inserted (duplicates are not counted).</returns>
    Task<int> BulkInsertIgnoreDuplicatesAsync(IReadOnlyList<Review> reviews, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches up to <paramref name="limit"/> reviews of one target that still have a
    /// <see langword="null"/> <see cref="Review.Embedding"/>, oldest
    /// <see cref="Review.DatePosted"/> first, so the embedding job's batches are
    /// deterministic across runs.
    /// </summary>
    /// <param name="targetId">The competitor target to fetch reviews for.</param>
    /// <param name="limit">The maximum number of reviews to return; must be positive.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be canceled.</param>
    /// <returns>Up to <paramref name="limit"/> unembedded reviews, oldest first.</returns>
    Task<IReadOnlyList<Review>> GetUnembeddedAsync(Guid targetId, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches every review of one target, ordered by <see cref="Review.DatePosted"/>
    /// then <see cref="Review.SourceReviewId"/> for a deterministic order.
    /// </summary>
    /// <param name="targetId">The competitor target to fetch reviews for.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be canceled.</param>
    /// <returns>All reviews of the target in posted order.</returns>
    Task<IReadOnlyList<Review>> GetByTargetIdAsync(Guid targetId, CancellationToken cancellationToken = default);
}
