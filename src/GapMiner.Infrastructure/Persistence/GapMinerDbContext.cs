using GapMiner.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GapMiner.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the GapMiner schema. All entity mapping is delegated to the
/// per-entity <see cref="IEntityTypeConfiguration{TEntity}"/> classes in
/// <c>Configurations/</c>; database identifiers follow the plan's snake_case
/// convention. Register with
/// <c>UseNpgsql(connectionString, o =&gt; o.UseVector())</c> so the pgvector
/// <c>vector(3072)</c> column type can be read and written.
/// </summary>
public sealed class GapMinerDbContext(DbContextOptions<GapMinerDbContext> options) : DbContext(options)
{
    /// <summary>Gets the tracked competitor products.</summary>
    public DbSet<CompetitorTarget> CompetitorTargets => Set<CompetitorTarget>();

    /// <summary>Gets the scraped reviews.</summary>
    public DbSet<Review> Reviews => Set<Review>();

    /// <summary>Gets the mined feature gaps.</summary>
    public DbSet<FeatureGap> FeatureGaps => Set<FeatureGap>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // pgvector: the InitialSchema migration emits CREATE EXTENSION IF NOT EXISTS
        // "vector" (the AppHost and the integration tests run the pgvector/pgvector:pg16
        // image, which ships the extension).
        modelBuilder.HasPostgresExtension("vector");

        // One IEntityTypeConfiguration per entity, discovered from this assembly.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GapMinerDbContext).Assembly);
    }
}
