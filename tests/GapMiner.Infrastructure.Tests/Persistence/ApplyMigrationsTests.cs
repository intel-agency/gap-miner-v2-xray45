using GapMiner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Persistence;

/// <summary>
/// Story 2.2 acceptance: the dev-only <see cref="GapMinerDbContextExtensions.ApplyMigrationsAsync"/>
/// startup extension creates the physical database when missing (Aspire 8.x AddDatabase
/// does not) and applies pending migrations, idempotently.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ApplyMigrationsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ApplyMigrationsAsync_creates_missing_database_and_applies_migrations()
    {
        // A database the container never created: exercises the CREATE DATABASE path.
        var databaseName = $"gapminer_apply_{Guid.NewGuid().ToString("N")[..8]}";
        using var provider = BuildProvider(databaseName);

        await provider.ApplyMigrationsAsync();

        using var scope = provider.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<GapMinerDbContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync();
        // Every migration defined in the assembly got applied (not a hardcoded
        // count — later migrations must not break this test), starting with
        // the initial schema.
        Assert.Equal(
            context.Database.GetMigrations().OrderBy(migration => migration, StringComparer.Ordinal),
            applied.OrderBy(migration => migration, StringComparer.Ordinal));
        Assert.Contains(applied, migration => migration.EndsWith("InitialSchema", StringComparison.Ordinal));
        Assert.Contains(applied, migration => migration.EndsWith("AddCompetitorTargetLastScrapedAt", StringComparison.Ordinal));
        Assert.True(await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.TableExists("review")));
    }

    [Fact]
    public async Task ApplyMigrationsAsync_is_idempotent_when_database_already_exists()
    {
        var databaseName = $"gapminer_apply_{Guid.NewGuid().ToString("N")[..8]}";
        using var provider = BuildProvider(databaseName);

        // First call creates the database; second call takes the exists-branch and
        // must succeed without re-applying anything.
        await provider.ApplyMigrationsAsync();
        await provider.ApplyMigrationsAsync();

        using var scope = provider.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<GapMinerDbContext>();
        var applied = await context.Database.GetAppliedMigrationsAsync();
        // Idempotent: the second call applied nothing new — still exactly the
        // assembly's full migration list.
        Assert.Equal(
            context.Database.GetMigrations().OrderBy(migration => migration, StringComparer.Ordinal),
            applied.OrderBy(migration => migration, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ApplyMigrationsAsync_throws_without_connection_string()
    {
        using var provider = new ServiceCollection()
            .AddDbContext<GapMinerDbContext>(options => options
                .UseNpgsql((string?)null, npgsql => npgsql.UseVector()))
            .BuildServiceProvider();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ApplyMigrationsAsync());

        Assert.Contains("no connection string", exception.Message, StringComparison.Ordinal);
    }

    private ServiceProvider BuildProvider(string databaseName)
    {
        var connectionString = new Npgsql.NpgsqlConnectionStringBuilder(fixture.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;

        return new ServiceCollection()
            .AddDbContext<GapMinerDbContext>(options => options
                .UseNpgsql(connectionString, npgsql => npgsql.UseVector()))
            .BuildServiceProvider();
    }
}
