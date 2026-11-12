using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GapMiner.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the repo-local <c>dotnet ef</c> tool
/// (.config/dotnet-tools.json) when generating migrations for this class library,
/// which has no host of its own. The placeholder connection string is sufficient:
/// <c>migrations add</c> only builds the model and never connects. Database updates
/// run through <see cref="GapMinerDbContextExtensions.ApplyMigrationsAsync"/> or the
/// Testcontainers integration tests.
/// </summary>
public sealed class GapMinerDbContextDesignFactory : IDesignTimeDbContextFactory<GapMinerDbContext>
{
    private const string PlaceholderConnectionString =
        "Host=localhost;Port=5432;Database=gapminer";

    /// <inheritdoc />
    public GapMinerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GapMinerDbContext>()
            .UseNpgsql(PlaceholderConnectionString, npgsql => npgsql.UseVector())
            .Options;

        return new GapMinerDbContext(options);
    }
}
