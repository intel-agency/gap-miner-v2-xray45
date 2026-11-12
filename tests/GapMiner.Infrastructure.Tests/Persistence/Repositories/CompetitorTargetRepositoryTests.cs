using GapMiner.Domain.Entities;
using GapMiner.Domain.Enums;
using GapMiner.Infrastructure.Persistence;
using GapMiner.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// Story 2.3 acceptance for <see cref="ICompetitorTargetRepository"/>: one
/// Testcontainers-verified test per interface method
/// (<c>AddAsync</c>, <c>GetByIdAsync</c>, <c>GetAllAsync</c>,
/// <c>ExistsByUrlAsync</c>) plus cancellation propagation. Runs against its own
/// migrated database on the shared pgvector/pgvector:pg16 container.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CompetitorTargetRepositoryTests(PostgresFixture fixture)
{
    private const string DatabaseName = "gapminer_repositories_targets";
    private const string GetAllDatabaseName = "gapminer_repositories_targets_all";

    [Fact]
    public async Task AddAsync_persists_the_target_and_GetByIdAsync_returns_it()
    {
        var target = NewTarget("Rank Tracker Pro", "https://apps.shopify.com/rank-tracker-pro", createdAt: BaseTime);

        await using (var context = await NewContextAsync())
        {
            var repository = new CompetitorTargetRepository(context);
            await repository.AddAsync(target);
        }

        await using (var context = await NewContextAsync())
        {
            var repository = new CompetitorTargetRepository(context);
            var fetched = await repository.GetByIdAsync(target.Id);

            Assert.NotNull(fetched);
            Assert.Equal(target.Id, fetched.Id);
            Assert.Equal("Rank Tracker Pro", fetched.Name);
            Assert.Equal(MarketplaceKind.ShopifyAppStore, fetched.MarketplaceKind);
            Assert.Equal(target.MarketplaceUrl, fetched.MarketplaceUrl);
            Assert.Equal(BaseTime, fetched.CreatedAt);
        }
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_an_unknown_id()
    {
        await using var context = await NewContextAsync();
        var repository = new CompetitorTargetRepository(context);

        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetAllAsync_returns_targets_ordered_by_created_at_then_name()
    {
        // Dedicated database: GetAllAsync is unfiltered, so the exact list is only
        // assertable when the ranking contains exactly this test's three targets.
        // Two targets share a CreatedAt so the Name tie-break is exercised.
        var middle = NewTarget("Middle Target", "https://apps.shopify.com/target-middle", createdAt: BaseTime);
        var late = NewTarget("Late Target", "https://apps.shopify.com/target-late", createdAt: BaseTime.AddHours(1));
        var alphabeticalFirst = NewTarget("A First Target", "https://apps.shopify.com/target-first", createdAt: BaseTime);

        await using (var context = await RepositoryTestDatabase.CreateContextAsync(fixture, GetAllDatabaseName))
        {
            var repository = new CompetitorTargetRepository(context);
            await repository.AddAsync(middle);
            await repository.AddAsync(late);
            await repository.AddAsync(alphabeticalFirst);
        }

        await using (var context = await RepositoryTestDatabase.CreateContextAsync(fixture, GetAllDatabaseName))
        {
            var repository = new CompetitorTargetRepository(context);
            var all = await repository.GetAllAsync();

            Assert.Equal(3, all.Count);
            // Same CreatedAt: "A First Target" sorts before "Middle Target" by name.
            Assert.Equal([alphabeticalFirst.Id, middle.Id, late.Id], all.Select(target => target.Id));
        }
    }

    [Fact]
    public async Task ExistsByUrlAsync_is_true_only_for_a_tracked_url()
    {
        var target = NewTarget("Exists probe", "https://apps.shopify.com/exists-probe");

        await using (var context = await NewContextAsync())
        {
            var repository = new CompetitorTargetRepository(context);
            await repository.AddAsync(target);
        }

        await using (var context = await NewContextAsync())
        {
            var repository = new CompetitorTargetRepository(context);

            Assert.True(await repository.ExistsByUrlAsync(target.MarketplaceUrl));
            Assert.False(await repository.ExistsByUrlAsync(new Uri("https://apps.shopify.com/never-added")));
        }
    }

    [Fact]
    public async Task GetAllAsync_honors_a_canceled_token()
    {
        await using var context = await NewContextAsync();
        var repository = new CompetitorTargetRepository(context);

        using var canceled = new CancellationTokenSource(TimeSpan.Zero);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.GetAllAsync(canceled.Token));
    }

    [Fact]
    public void AddGapMinerRepositories_registers_all_three_repositories_scoped()
    {
        var connectionString = fixture.GetConnectionString(DatabaseName);

        using var provider = new ServiceCollection()
            .AddDbContext<GapMinerDbContext>(options => options
                .UseNpgsql(connectionString, npgsql => npgsql.UseVector()))
            .AddGapMinerRepositories()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();

        Assert.IsType<CompetitorTargetRepository>(scope.ServiceProvider.GetService<ICompetitorTargetRepository>());
        Assert.IsType<ReviewRepository>(scope.ServiceProvider.GetService<IReviewRepository>());
        Assert.IsType<FeatureGapRepository>(scope.ServiceProvider.GetService<IFeatureGapRepository>());

        // Scoped lifetime: two resolutions inside one scope share the instance (and
        // therefore the DbContext); a different scope gets a fresh one.
        Assert.Same(
            scope.ServiceProvider.GetService<IReviewRepository>(),
            scope.ServiceProvider.GetService<IReviewRepository>());
        using var otherScope = provider.CreateScope();
        Assert.NotSame(
            scope.ServiceProvider.GetService<IReviewRepository>(),
            otherScope.ServiceProvider.GetService<IReviewRepository>());
    }

    private async Task<GapMinerDbContext> NewContextAsync()
        => await RepositoryTestDatabase.CreateContextAsync(fixture, DatabaseName);

    private static readonly DateTimeOffset BaseTime =
        new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    private static CompetitorTarget NewTarget(string name, string url, DateTimeOffset? createdAt = null) => new(
        Guid.NewGuid(),
        name,
        MarketplaceKind.ShopifyAppStore,
        new Uri(url),
        createdAt ?? DateTimeOffset.UtcNow);
}
