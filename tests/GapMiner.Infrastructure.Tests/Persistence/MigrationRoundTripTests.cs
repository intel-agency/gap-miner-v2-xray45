using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Persistence;

/// <summary>
/// Story 2.2 acceptance: the <c>InitialSchema</c> migration applies cleanly against
/// PostgreSQL 16 + pgvector, and the story's rollback validation holds —
/// <c>database update 0</c> (MigrateAsync("0")) reverts the schema with no orphaned
/// objects, and re-applying leaves a working schema again.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationRoundTripTests(PostgresFixture fixture)
{
    [Fact]
    public async Task InitialSchema_applies_and_reverts_cleanly_on_postgres16_pgvector()
    {
        // --- apply ------------------------------------------------------------
        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        // --- the schema PostgreSQL actually persisted -------------------------
        await using (var context = fixture.CreateContext())
        {
            Assert.True(
                await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.ExtensionExists("vector")),
                "the vector extension should be installed");
            Assert.True(await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.TableExists("competitor_target")));
            Assert.True(await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.TableExists("review")));
            Assert.True(await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.TableExists("feature_gap")));

            Assert.True(
                await PostgresProbe.ScalarBoolAsync(
                    context,
                    PostgresProbe.UniqueIndexExists("review", "ux_review_competitor_target_id_source_review_id")),
                "the idempotency index should exist and be unique");

            Assert.True(
                await PostgresProbe.ScalarBoolAsync(
                    context,
                    PostgresProbe.IndexExists("feature_gap", "ix_feature_gap_severity_score_mention_frequency")),
                "the ranked-gaps index should exist");

            var rankedIndexDefinition = await PostgresProbe.ScalarStringAsync(
                context,
                PostgresProbe.IndexDefinition("ix_feature_gap_severity_score_mention_frequency"));
            Assert.Contains("severity_score DESC", rankedIndexDefinition);
            Assert.Contains("mention_frequency DESC", rankedIndexDefinition);
        }

        // --- rollback: database update 0 --------------------------------------
        // EF 8's facade MigrateAsync() has no target-migration overload (added in
        // EF 9); the IMigrator service is the EF 8 way to migrate to "0".
        await using (var context = fixture.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("0");
        }

        // --- no orphaned objects ----------------------------------------------
        await using (var context = fixture.CreateContext())
        {
            Assert.False(
                await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.ExtensionExists("vector")),
                "update 0 should drop the vector extension created by Up");
            Assert.False(await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.TableExists("review")));
            Assert.False(await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.TableExists("feature_gap")));
            Assert.False(await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.TableExists("competitor_target")));
            Assert.False(
                await PostgresProbe.ScalarBoolAsync(
                    context,
                    PostgresProbe.IndexExists("review", "ux_review_competitor_target_id_source_review_id")));
        }

        // --- re-apply ----------------------------------------------------------
        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync();
            Assert.True(await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.TableExists("review")));
            Assert.True(
                await PostgresProbe.ScalarBoolAsync(context, PostgresProbe.ExtensionExists("vector")));
        }
    }
}
