using System.Data.Common;
using GapMiner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GapMiner.Infrastructure.Tests.Persistence;

/// <summary>
/// Raw-catalog probes against a live <see cref="GapMinerDbContext"/> connection,
/// used by the Testcontainers tests to assert what PostgreSQL actually persisted.
/// </summary>
internal static class PostgresProbe
{
    public static async Task<bool> ScalarBoolAsync(
        GapMinerDbContext context,
        string sql,
        CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return Convert.ToBoolean(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string ExtensionExists(string extensionName)
        => $"SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_extension WHERE extname = '{extensionName}')";

    public static string TableExists(string tableName)
        => $"SELECT to_regclass('public.{tableName}') IS NOT NULL";

    public static string IndexExists(string tableName, string indexName)
        => "SELECT EXISTS (" +
           "SELECT 1 FROM pg_catalog.pg_index i " +
           "JOIN pg_catalog.pg_class c ON i.indexrelid = c.oid " +
           "JOIN pg_catalog.pg_class t ON i.indrelid = t.oid " +
           $"WHERE c.relname = '{indexName}' AND t.relname = '{tableName}')";

    public static string UniqueIndexExists(string tableName, string indexName)
        => "SELECT EXISTS (" +
           "SELECT 1 FROM pg_catalog.pg_index i " +
           "JOIN pg_catalog.pg_class c ON i.indexrelid = c.oid " +
           "JOIN pg_catalog.pg_class t ON i.indrelid = t.oid " +
           $"WHERE c.relname = '{indexName}' AND t.relname = '{tableName}' AND i.indisunique)";

    public static string IndexDefinition(string indexName)
        => $"SELECT pg_get_indexdef(c.oid) FROM pg_catalog.pg_class c WHERE c.relname = '{indexName}'";

    public static async Task<string> ScalarStringAsync(
        GapMinerDbContext context,
        string sql,
        CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
