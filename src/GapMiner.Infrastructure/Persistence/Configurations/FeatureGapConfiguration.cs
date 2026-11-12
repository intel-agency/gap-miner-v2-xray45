using GapMiner.Domain.Entities;
using GapMiner.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GapMiner.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="FeatureGap"/> to the snake_case <c>feature_gap</c> table, with the
/// ranked-gaps index on <c>(severity_score DESC, mention_frequency DESC)</c> that backs
/// "most severe / most mentioned" queries.
/// </summary>
public sealed class FeatureGapConfiguration : IEntityTypeConfiguration<FeatureGap>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<FeatureGap> builder)
    {
        builder.ToTable("feature_gap");

        builder.HasKey(gap => gap.Id);
        builder.Property(gap => gap.Id).HasColumnName("id");

        builder.Property(gap => gap.CompetitorTargetId)
               .HasColumnName("competitor_target_id");

        builder.Property(gap => gap.Title)
               .HasColumnName("title")
               .IsRequired();

        builder.Property(gap => gap.DetailedDescription)
               .HasColumnName("detailed_description")
               .IsRequired();

        // The [1.0, 10.0] range is enforced by the SeverityScore factory on write;
        // the converter persists just the wrapped double.
        builder.Property(gap => gap.SeverityScore)
               .HasColumnName("severity_score")
               .HasConversion(
                   severity => severity.Value,
                   value => SeverityScore.Create(value));

        builder.Property(gap => gap.MentionFrequency)
               .HasColumnName("mention_frequency");

        builder.Property(gap => gap.SuggestedTechStack)
               .HasColumnName("suggested_tech_stack")
               .IsRequired();

        builder.Property(gap => gap.ActionableImplementationPlan)
               .HasColumnName("actionable_implementation_plan")
               .IsRequired();

        builder.Property(gap => gap.IdentifiedAt)
               .HasColumnName("identified_at");

        // Ranked gaps: most severe first, most mentioned first.
        builder.HasIndex(gap => new { gap.SeverityScore, gap.MentionFrequency })
               .HasDatabaseName("ix_feature_gap_severity_score_mention_frequency")
               .IsDescending(true, true);

        // Unlike review, no composite index leads with competitor_target_id here, so
        // the FK convention index gets an explicit snake_case name.
        builder.HasIndex(gap => gap.CompetitorTargetId)
               .HasDatabaseName("ix_feature_gap_competitor_target_id");

        builder.HasOne<CompetitorTarget>()
               .WithMany()
               .HasForeignKey(gap => gap.CompetitorTargetId);
    }
}
