using GapMiner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Persistence.Repositories;

/// <summary>
/// Creates migrated, repository-test-owned databases on the shared
/// <see cref="PostgresFixture"/> container. Each repository test class uses its own
/// database name (created + migrated once per process) so the unfiltered queries
/// under test — <c>GetAllAsync</c>, <c>GetRankedAsync</c> — see only their own rows
/// and exact-set assertions stay valid no matter what the other classes inserted.
/// </summary>
internal static class RepositoryTestDatabase
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> MigratedDatabases = new();

    public static async Task<GapMinerDbContext> CreateContextAsync(PostgresFixture fixture, string databaseName)
    {
        await Gate.WaitAsync();
        try
        {
            if (MigratedDatabases.Add(databaseName))
            {
                var connectionString = new NpgsqlConnectionStringBuilder(fixture.GetConnectionString())
                {
                    Database = databaseName,
                }.ConnectionString;

                // The same dev-startup path the Api uses: create the database when
                // missing, then apply the InitialSchema migration to it.
                using var provider = new ServiceCollection()
                    .AddDbContext<GapMinerDbContext>(options => options
                        .UseNpgsql(connectionString, npgsql => npgsql.UseVector()))
                    .BuildServiceProvider();

                await provider.ApplyMigrationsAsync();
            }
        }
        finally
        {
            Gate.Release();
        }

        return fixture.CreateContext(databaseName);
    }
}
