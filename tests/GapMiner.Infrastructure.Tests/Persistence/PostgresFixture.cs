using GapMiner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Persistence;

/// <summary>
/// One shared PostgreSQL 16 + pgvector container for the integration tests — the same
/// pgvector/pgvector:pg16 image the AppHost runs. xunit runs every class in the
/// <see cref="PostgresCollection"/> sequentially against it; the tests use distinct
/// database names where they mutate database state.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .WithDatabase("gapminer")
        .Build();

    public string GetConnectionString(string? database = null)
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(container.GetConnectionString());
        if (database is not null)
        {
            builder.Database = database;
        }

        return builder.ConnectionString;
    }

    public GapMinerDbContext CreateContext(string? database = null)
    {
        var options = new DbContextOptionsBuilder<GapMinerDbContext>()
            .UseNpgsql(GetConnectionString(database), npgsql => npgsql.UseVector())
            .Options;

        return new GapMinerDbContext(options);
    }

    Task IAsyncLifetime.InitializeAsync() => container.StartAsync();

    Task IAsyncLifetime.DisposeAsync() => container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres integration";
}
