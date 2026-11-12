using GapMiner.Domain.Entities;
using Xunit;

namespace GapMiner.Domain.Tests;

public class ReviewTests
{
    private static Review CreateReview(int starRating = 4, string reviewText = "Great product!") =>
        new(
            id: Guid.NewGuid(),
            competitorTargetId: Guid.NewGuid(),
            starRating: starRating,
            reviewText: reviewText,
            reviewAuthor: "Jane Doe",
            datePosted: new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero),
            sourceReviewId: "ext-12345",
            embedding: new float[] { 0.1f, 0.25f, -0.3f });

    [Fact]
    public void Constructor_AssignsAllProperties()
    {
        var id = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var posted = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);
        var embedding = new float[] { 0.5f };

        var review = new Review(
            id: id,
            competitorTargetId: targetId,
            starRating: 3,
            reviewText: "Solid, but lacks bulk export.",
            reviewAuthor: "Ada",
            datePosted: posted,
            sourceReviewId: "g2-98765",
            embedding: embedding);

        Assert.Equal(id, review.Id);
        Assert.Equal(targetId, review.CompetitorTargetId);
        Assert.Equal(3, review.StarRating);
        Assert.Equal("Solid, but lacks bulk export.", review.ReviewText);
        Assert.Equal("Ada", review.ReviewAuthor);
        Assert.Equal(posted, review.DatePosted);
        Assert.Equal("g2-98765", review.SourceReviewId);
        Assert.Same(embedding, review.Embedding);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Constructor_AcceptsStarRatingsWithinInclusiveRange(int starRating)
    {
        var review = CreateReview(starRating: starRating);

        Assert.Equal(starRating, review.StarRating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public void Constructor_RejectsStarRatingsOutsideOneToFive(int starRating)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateReview(starRating: starRating));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsEmptyReviewText(string reviewText)
    {
        Assert.ThrowsAny<ArgumentException>(() => CreateReview(reviewText: reviewText));
    }

    [Fact]
    public void Constructor_RejectsNullReviewText()
    {
        Assert.Throws<ArgumentNullException>(
            () => CreateReview(reviewText: null!));
    }

    [Fact]
    public void Constructor_EmbddingDefaultsToNull()
    {
        var review = new Review(
            id: Guid.NewGuid(),
            competitorTargetId: Guid.NewGuid(),
            starRating: 5,
            reviewText: "Best in class.",
            reviewAuthor: "Grace",
            datePosted: DateTimeOffset.UtcNow,
            sourceReviewId: "chr-42");

        Assert.Null(review.Embedding);
    }
}
