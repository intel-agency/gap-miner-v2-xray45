using GapMiner.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GapMiner.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="CompetitorTarget"/> to the snake_case <c>competitor_target</c>
/// table (plan §5: database columns are snake_case, e.g. <c>marketplace_url</c>).
/// </summary>
public sealed class CompetitorTargetConfiguration : IEntityTypeConfiguration<CompetitorTarget>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CompetitorTarget> builder)
    {
        builder.ToTable("competitor_target");

        builder.HasKey(target => target.Id);
        builder.Property(target => target.Id).HasColumnName("id");

        builder.Property(target => target.Name)
               .HasColumnName("name")
               .IsRequired();

        // Enum persisted as its name (text) — readable in ad-hoc queries and reports.
        builder.Property(target => target.MarketplaceKind)
               .HasColumnName("marketplace_kind")
               .HasConversion<string>()
               .IsRequired();

        builder.Property(target => target.MarketplaceUrl)
               .HasColumnName("marketplace_url")
               .IsRequired();

        builder.Property(target => target.CreatedAt)
               .HasColumnName("created_at");
    }
}
