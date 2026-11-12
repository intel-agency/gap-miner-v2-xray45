using GapMiner.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pgvector;

namespace GapMiner.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Review"/> to the snake_case <c>review</c> table, including the
/// pgvector <c>vector(3072)</c> embedding column for text-embedding-3-large and the
/// unique composite <c>(competitor_target_id, source_review_id)</c> index that makes
/// re-scrapes idempotent.
/// </summary>
public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("review");

        builder.HasKey(review => review.Id);
        builder.Property(review => review.Id).HasColumnName("id");

        builder.Property(review => review.CompetitorTargetId)
               .HasColumnName("competitor_target_id");

        builder.Property(review => review.StarRating)
               .HasColumnName("star_rating");

        builder.Property(review => review.ReviewText)
               .HasColumnName("review_text")
               .IsRequired();

        builder.Property(review => review.ReviewAuthor)
               .HasColumnName("review_author")
               .IsRequired();

        builder.Property(review => review.DatePosted)
               .HasColumnName("date_posted");

        builder.Property(review => review.SourceReviewId)
               .HasColumnName("source_review_id")
               .IsRequired();

        // pgvector: the domain owns float[]; the column is vector(3072) for
        // text-embedding-3-large. The converter bridges to Pgvector's Vector type;
        // the value comparer makes change tracking compare by element, not by array
        // reference (each materialization produces a new array instance).
        builder.Property(review => review.Embedding)
               .HasColumnName("embedding")
               .HasColumnType("vector(3072)")
               .HasConversion(
                   new ValueConverter<float[]?, Vector?>(
                       value => value == null ? null : new Vector(value),
                       value => value == null ? null : value.ToArray()),
                   new ValueComparer<float[]?>(
                       (a, b) => (a == null && b == null)
                           || (a != null && b != null && a.SequenceEqual(b)),
                       a => a == null ? 0 : a.Select(element => element.GetHashCode())
                           .Aggregate(                               HashCode.Combine(17),                               (accumulated, code) => HashCode.Combine(accumulated, code)),
                       a => a == null ? null : a.ToArray()));

        // Idempotency: (competitor_target_id, source_review_id) uniquely identifies a
        // review, so re-scraping the same marketplace review can never duplicate rows.
        // Being the leading column of this index also serves the FK, so no separate
        // competitor_target_id index is created for review.
        builder.HasIndex(review => new { review.CompetitorTargetId, review.SourceReviewId })
               .HasDatabaseName("ux_review_competitor_target_id_source_review_id")
               .IsUnique();

        builder.HasOne<CompetitorTarget>()
               .WithMany()
               .HasForeignKey(review => review.CompetitorTargetId);
    }
}
