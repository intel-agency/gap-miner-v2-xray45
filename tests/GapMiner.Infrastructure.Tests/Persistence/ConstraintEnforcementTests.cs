using GapMiner.Domain.Entities;
using GapMiner.Domain.Enums;
using GapMiner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Persistence;

/// <summary>
/// Story 2.2 acceptance, behavioral half: PostgreSQL must actually enforce the
/// schema the model declares — the unique idempotency index rejects a second row
/// with the same (competitor_target_id, source_review_id) with 23505, and the
/// vector(3072) column rejects embeddings of any other dimension with 22000.
/// The catalog half (indexes exist with the right shape) lives in
/// <see cref="MigrationRoundTripTests"/>; these tests use a dedicated database
/// name so shared-container migration state cannot affect them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConstraintEnforcementTests(PostgresFixture fixture)
{
    private const string DatabaseName = "gapminer_constraints";

    [Fact]
    public async Task Unique_idempotency_index_rejects_duplicate_reviews()
    {
        await using var context = await CreateMigratedContextAsync();

        var target = NewTarget("Constraint probe", "https://apps.shopify.com/constraint-probe");
        context.Add(target);
        await context.SaveChangesAsync();

        context.Add(NewReview(target.Id, sourceReviewId: "src-duplicate"));
        await context.SaveChangesAsync();

        // Same (competitor_target_id, source_review_id), different row: the unique
        // composite index ux_review_competitor_target_id_source_review_id must
        // reject it — this is what makes re-scrapes idempotent at the database.
        context.Add(NewReview(target.Id, sourceReviewId: "src-duplicate"));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", postgresException.SqlState);
    }

    [Fact]
    public async Task Embedding_column_rejects_wrong_dimension_vectors()
    {
        await using var context = await CreateMigratedContextAsync();

        var target = NewTarget("Dimension probe", "https://chromewebstore.google.com/dimension-probe");

        // The column is vector(3072) for text-embedding-3-large; a 3-element
        // embedding must be rejected by PostgreSQL, not silently truncated.
        var review = new Review(
            Guid.NewGuid(),
            target.Id,
            starRating: 5,
            reviewText: "wrong-dimension embedding probe",
            reviewAuthor: "probe",
            datePosted: DateTimeOffset.UtcNow,
            sourceReviewId: "src-dimension",
            embedding: new float[3]);

        context.Add(target);
        context.Add(review);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("22000", postgresException.SqlState);
    }

    /// <summary>
    /// Creates the physical database when missing and migrates it via the same
    /// dev-startup path the Api uses, then returns a fixture-built context on it.
    /// </summary>
    private async Task<GapMinerDbContext> CreateMigratedContextAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.GetConnectionString())
        {
            Database = DatabaseName,
        }.ConnectionString;

        using var provider = new ServiceCollection()
            .AddDbContext<GapMinerDbContext>(options => options
                .UseNpgsql(connectionString, npgsql => npgsql.UseVector()))
            .BuildServiceProvider();

        await provider.ApplyMigrationsAsync();

        return fixture.CreateContext(DatabaseName);
    }

    private static CompetitorTarget NewTarget(string name, string url) => new(
        Guid.NewGuid(),
        name,
        MarketplaceKind.ShopifyAppStore,
        new Uri(url),
        DateTimeOffset.UtcNow);

    private static Review NewReview(Guid competitorTargetId, string sourceReviewId) => new(
        Guid.NewGuid(),
        competitorTargetId,
        starRating: 4,
        reviewText: "constraint enforcement probe",
        reviewAuthor: "probe",
        datePosted: DateTimeOffset.UtcNow,
        sourceReviewId: sourceReviewId);
}
