namespace GapMiner.Domain.Entities;

/// <summary>
/// A single customer review scraped from a competitor's marketplace listing.
/// <see cref="SourceReviewId"/> is the external idempotency key: the composite
/// (CompetitorTargetId, SourceReviewId) uniquely identifies a review so
/// re-scrapes never duplicate rows (enforced by the persistence layer, Story 2.2).
/// Immutable after creation.
/// </summary>
public sealed class Review
{
    /// <summary>Gets the aggregate identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the tracked competitor the review belongs to.</summary>
    public Guid CompetitorTargetId { get; init; }

    /// <summary>Gets the star rating; validated to the inclusive range [1, 5].</summary>
    public int StarRating { get; init; }

    /// <summary>Gets the review body; must be non-empty.</summary>
    public string ReviewText { get; init; }

    /// <summary>Gets the review author's display name.</summary>
    public string ReviewAuthor { get; init; }

    /// <summary>Gets the UTC moment the review was posted on the marketplace.</summary>
    public DateTimeOffset DatePosted { get; init; }

    /// <summary>Gets the external idempotency key assigned by the marketplace.</summary>
    public string SourceReviewId { get; init; }

    /// <summary>
    /// Gets the semantic embedding of <see cref="ReviewText"/> (pgvector vector(3072)
    /// in persistence), or <see langword="null"/> until the AI worker embeds the review.
    /// </summary>
    public float[]? Embedding { get; init; }

    public Review(
        Guid id,
        Guid competitorTargetId,
        int starRating,
        string reviewText,
        string reviewAuthor,
        DateTimeOffset datePosted,
        string sourceReviewId,
        float[]? embedding = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(starRating, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(starRating, 5);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewText);

        Id = id;
        CompetitorTargetId = competitorTargetId;
        StarRating = starRating;
        ReviewText = reviewText;
        ReviewAuthor = reviewAuthor;
        DatePosted = datePosted;
        SourceReviewId = sourceReviewId;
        Embedding = embedding;
    }
}
