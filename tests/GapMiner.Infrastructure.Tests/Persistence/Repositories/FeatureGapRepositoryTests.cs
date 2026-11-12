using GapMiner.Domain.Entities;
using GapMiner.Domain.Enums;
using GapMiner.Domain.ValueObjects;
using GapMiner.Infrastructure.Persistence;
using GapMiner.Infrastructure.Persistence.Repositories;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// Story 2.3 acceptance for <see cref="IFeatureGapRepository"/>: one
/// Testcontainers-verified test per interface method — <c>AddAsync</c>,
/// <c>GetRankedAsync(sortBy, skip, take)</c> (both rankings, the severity
/// tie-break, and pagination) and <c>GetByTargetIdAsync</c> — plus argument
/// validation and cancellation propagation. Ranked tests give every batch its
/// own competitor target and assert within it (the ranking is global); the
/// pagination test runs against a dedicated database so page boundaries are
/// unambiguous.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FeatureGapRepositoryTests(PostgresFixture fixture)
{
    private const string DatabaseName = "gapminer_repositories_gaps";
    private const string PaginationDatabaseName = "gapminer_repositories_gaps_page";

    private static readonly DateTimeOffset BaseTime =
        new(2026, 11, 12, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_persists_the_gap_and_GetByTargetIdAsync_returns_it()
    {
        var target = await AddTargetAsync(DatabaseName, "Gap persistence probe");
        var otherTarget = await AddTargetAsync(DatabaseName, "Gap persistence other");

        var earliest = NewGap(target.Id, "Earliest gap", severity: 5.0, mentions: 3, identifiedAt: BaseTime);
        var latest = NewGap(target.Id, "Latest gap", severity: 8.0, mentions: 9, identifiedAt: BaseTime.AddDays(2));
        var foreignGap = NewGap(otherTarget.Id, "Other target's gap", severity: 1.5, mentions: 1, identifiedAt: BaseTime);

        await using (var context = await NewContextAsync(DatabaseName))
        {
            var repository = new FeatureGapRepository(context);
            await repository.AddAsync(latest);
            await repository.AddAsync(earliest);
            await repository.AddAsync(foreignGap);
        }

        await using (var context = await NewContextAsync(DatabaseName))
        {
            var repository = new FeatureGapRepository(context);
            var stored = await repository.GetByTargetIdAsync(target.Id);

            // Only the target's own gaps, in identification order.
            Assert.Equal([earliest.Id, latest.Id], stored.Select(gap => gap.Id));

            var roundTripped = Assert.Single(stored, gap => gap.Id == latest.Id);
            Assert.Equal("Latest gap", roundTripped.Title);
            Assert.Equal("gap probe description", roundTripped.DetailedDescription);
            Assert.Equal(8.0, roundTripped.SeverityScore.Value);
            Assert.Equal(9, roundTripped.MentionFrequency);
            Assert.Equal("gap probe stack", roundTripped.SuggestedTechStack);
            Assert.Equal("gap probe plan", roundTripped.ActionableImplementationPlan);
        }
    }

    [Fact]
    public async Task GetRankedAsync_by_severity_orders_severity_desc_then_mention_frequency_desc()
    {
        var target = await AddTargetAsync(DatabaseName, "Severity ranking probe");
        await AddGapsAsync(DatabaseName,
            NewGap(target.Id, "g-sev-mid", severity: 5.0, mentions: 1),
            NewGap(target.Id, "g-sev-top", severity: 9.0, mentions: 5),
            NewGap(target.Id, "g-sev-tie-low", severity: 9.0, mentions: 7),
            NewGap(target.Id, "g-sev-bottom", severity: 1.5, mentions: 99));

        await using var context = await NewContextAsync(DatabaseName);
        var repository = new FeatureGapRepository(context);

        // The ranking is global (no target filter — the dashboard lists all gaps),
        // so assert within this test's own target batch.
        var ranked = await repository.GetRankedAsync(FeatureGapSortBy.Severity, skip: 0, take: 100);

        Assert.Equal(
            new[] { "g-sev-tie-low", "g-sev-top", "g-sev-mid", "g-sev-bottom" },
            ranked.Where(gap => gap.CompetitorTargetId == target.Id).Select(gap => gap.Title));
    }

    [Fact]
    public async Task GetRankedAsync_by_frequency_orders_mention_frequency_desc_then_severity_desc()
    {
        var target = await AddTargetAsync(DatabaseName, "Frequency ranking probe");
        await AddGapsAsync(DatabaseName,
            NewGap(target.Id, "g-freq-top", severity: 1.5, mentions: 99),
            NewGap(target.Id, "g-freq-tie-high", severity: 9.0, mentions: 50),
            NewGap(target.Id, "g-freq-tie-low", severity: 5.0, mentions: 50),
            NewGap(target.Id, "g-freq-bottom", severity: 9.5, mentions: 1));

        await using var context = await NewContextAsync(DatabaseName);
        var repository = new FeatureGapRepository(context);

        var ranked = await repository.GetRankedAsync(FeatureGapSortBy.Frequency, skip: 0, take: 100);

        Assert.Equal(
            new[] { "g-freq-top", "g-freq-tie-high", "g-freq-tie-low", "g-freq-bottom" },
            ranked.Where(gap => gap.CompetitorTargetId == target.Id).Select(gap => gap.Title));
    }

    [Fact]
    public async Task GetRankedAsync_skips_and_takes_a_page_of_the_ranking()
    {
        // Dedicated database: skip/take page over ALL rows, so the page content is
        // only unambiguous when the ranking contains exactly this test's five gaps.
        var target = await AddTargetAsync(PaginationDatabaseName, "Pagination probe");
        await AddGapsAsync(PaginationDatabaseName,
            // Severities 9, 7, 5, 3, 1 with distinct mention frequencies: one total
            // ranking order, no ties, so the page boundaries are unambiguous.
            NewGap(target.Id, "g-page-1", severity: 9.0, mentions: 5),
            NewGap(target.Id, "g-page-2", severity: 7.0, mentions: 4),
            NewGap(target.Id, "g-page-3", severity: 5.0, mentions: 3),
            NewGap(target.Id, "g-page-4", severity: 3.0, mentions: 2),
            NewGap(target.Id, "g-page-5", severity: 1.0, mentions: 1));

        await using var context = await NewContextAsync(PaginationDatabaseName);
        var repository = new FeatureGapRepository(context);

        var page = await repository.GetRankedAsync(FeatureGapSortBy.Severity, skip: 1, take: 2);

        Assert.Equal(new[] { "g-page-2", "g-page-3" }, page.Select(gap => gap.Title));
    }

    [Fact]
    public async Task GetRankedAsync_rejects_invalid_arguments()
    {
        await using var context = await NewContextAsync(DatabaseName);
        var repository = new FeatureGapRepository(context);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repository.GetRankedAsync(FeatureGapSortBy.Severity, skip: -1, take: 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repository.GetRankedAsync(FeatureGapSortBy.Severity, skip: 0, take: 0));

        // An out-of-range enum cast must fail loudly, never fall through to a query.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => repository.GetRankedAsync((FeatureGapSortBy)99, skip: 0, take: 10));
    }

    [Fact]
    public async Task GetRankedAsync_honors_a_canceled_token()
    {
        await using var context = await NewContextAsync(DatabaseName);
        var repository = new FeatureGapRepository(context);

        using var canceled = new CancellationTokenSource(TimeSpan.Zero);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.GetRankedAsync(FeatureGapSortBy.Severity, skip: 0, take: 10, canceled.Token));
    }

    private async Task AddGapsAsync(string databaseName, params FeatureGap[] gaps)
    {
        await using var context = await NewContextAsync(databaseName);
        var repository = new FeatureGapRepository(context);

        foreach (var gap in gaps)
        {
            await repository.AddAsync(gap);
        }
    }

    private async Task<CompetitorTarget> AddTargetAsync(string databaseName, string name)
    {
        var target = new CompetitorTarget(
            Guid.NewGuid(),
            name,
            MarketplaceKind.ShopifyAppStore,
            new Uri($"https://apps.shopify.com/{name.ToLowerInvariant().Replace(' ', '-')}"),
            DateTimeOffset.UtcNow);

        await using var context = await NewContextAsync(databaseName);
        await new CompetitorTargetRepository(context).AddAsync(target);

        return target;
    }

    private async Task<GapMinerDbContext> NewContextAsync(string databaseName)
        => await RepositoryTestDatabase.CreateContextAsync(fixture, databaseName);

    private static FeatureGap NewGap(Guid competitorTargetId, string title, double severity, int mentions) =>
        NewGap(competitorTargetId, title, severity, mentions, BaseTime);

    private static FeatureGap NewGap(
        Guid competitorTargetId,
        string title,
        double severity,
        int mentions,
        DateTimeOffset identifiedAt) => new(
            Guid.NewGuid(),
            competitorTargetId,
            title,
            "gap probe description",
            SeverityScore.Create(severity),
            mentions,
            "gap probe stack",
            "gap probe plan",
            identifiedAt);
}
