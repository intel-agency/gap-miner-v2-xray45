using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GapMiner.Infrastructure.Persistence;

/// <summary>
/// Dev-only startup helper for <see cref="GapMinerDbContext"/>: ensures the physical
/// database exists (Aspire 8.x <c>AddDatabase</c> wires the connection string but does
/// not create the database) and applies all pending migrations.
/// </summary>
public static class GapMinerDbContextExtensions
{
    private const int MaxConnectionAttempts = 60;

    private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Creates the target database when missing and applies all pending migrations.
    /// Call once at application startup in development only (the Api does this under
    /// the AppHost, which waits for the postgres resource first); production applies
    /// migrations through the deployment pipeline instead.
    /// </summary>
    /// <param name="serviceProvider">The application's service provider.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static async Task ApplyMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GapMinerDbContext>();

        await EnsureDatabaseExistsAsync(dbContext, cancellationToken).ConfigureAwait(false);
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureDatabaseExistsAsync(
        GapMinerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var connectionString = dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                "GapMinerDbContext has no connection string. Register it with " +
                "UseNpgsql before applying migrations.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrEmpty(builder.Database))
        {
            throw new InvalidOperationException(
                "The GapMinerDbContext connection string has no database name.");
        }

        // Connect to the server's maintenance database instead of the target database,
        // which may not exist yet; CREATE DATABASE cannot run inside a transaction.
        // Aspire 8.2 has no WaitFor for this ordering, so the dev flow starts the Api
        // while the postgres container may still be booting — retry the connection,
        // bounded, so a genuinely unreachable server still fails loudly.
        await using var adminConnection = await OpenAdminConnectionAsync(
            connectionString, cancellationToken).ConfigureAwait(false);

        if (await DatabaseExistsAsync(adminConnection, builder.Database, cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        // Database names are configuration, not user input; they still cannot be
        // parameterized in DDL, so quote them defensively.
        await using var createCommand = adminConnection.CreateCommand();
        createCommand.CommandText =
            $"CREATE DATABASE \"{builder.Database.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        await createCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<NpgsqlConnection> OpenAdminConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres",
        };

        var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch (NpgsqlException) when (attempt < MaxConnectionAttempts)
            {
                await Task.Delay(ConnectionRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> DatabaseExistsAsync(
        NpgsqlConnection adminConnection,
        string databaseName,
        CancellationToken cancellationToken)
    {
        await using var command = adminConnection.CreateCommand();
        command.CommandText = "SELECT 1 FROM pg_catalog.pg_database WHERE datname = $1";
        command.Parameters.AddWithValue(databaseName);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }
}
