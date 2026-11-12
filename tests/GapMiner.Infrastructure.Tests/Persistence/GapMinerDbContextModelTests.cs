using GapMiner.Domain.Entities;
using GapMiner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Persistence;

/// <summary>
/// Story 2.2 acceptance, model level (no database needed): the three entities are
/// configured via IEntityTypeConfiguration with snake_case identifiers, the pgvector
/// extension is enabled on the model, Review.Embedding maps to vector(3072), the
/// idempotency index is unique on (competitor_target_id, source_review_id), and the
/// ranked-gaps index is descending.
/// </summary>
public sealed class GapMinerDbContextModelTests
{
    private static GapMinerDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GapMinerDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=gapminer", npgsql => npgsql.UseVector())
            .Options;

        return new GapMinerDbContext(options);
    }

    public static TheoryData<Type, string> SnakeCaseTables => new()
    {
        { typeof(CompetitorTarget), "competitor_target" },
        { typeof(Review), "review" },
        { typeof(FeatureGap), "feature_gap" },
    };

    public static TheoryData<Type, string, string> SnakeCaseColumns => new()
    {
        { typeof(CompetitorTarget), nameof(CompetitorTarget.Id), "id" },
        { typeof(CompetitorTarget), nameof(CompetitorTarget.Name), "name" },
        { typeof(CompetitorTarget), nameof(CompetitorTarget.MarketplaceKind), "marketplace_kind" },
        { typeof(CompetitorTarget), nameof(CompetitorTarget.MarketplaceUrl), "marketplace_url" },
        { typeof(CompetitorTarget), nameof(CompetitorTarget.CreatedAt), "created_at" },
        { typeof(Review), nameof(Review.Id), "id" },
        { typeof(Review), nameof(Review.CompetitorTargetId), "competitor_target_id" },
        { typeof(Review), nameof(Review.StarRating), "star_rating" },
        { typeof(Review), nameof(Review.ReviewText), "review_text" },
        { typeof(Review), nameof(Review.ReviewAuthor), "review_author" },
        { typeof(Review), nameof(Review.DatePosted), "date_posted" },
        { typeof(Review), nameof(Review.SourceReviewId), "source_review_id" },
        { typeof(Review), nameof(Review.Embedding), "embedding" },
        { typeof(FeatureGap), nameof(FeatureGap.Id), "id" },
        { typeof(FeatureGap), nameof(FeatureGap.CompetitorTargetId), "competitor_target_id" },
        { typeof(FeatureGap), nameof(FeatureGap.Title), "title" },
        { typeof(FeatureGap), nameof(FeatureGap.DetailedDescription), "detailed_description" },
        { typeof(FeatureGap), nameof(FeatureGap.SeverityScore), "severity_score" },
        { typeof(FeatureGap), nameof(FeatureGap.MentionFrequency), "mention_frequency" },
        { typeof(FeatureGap), nameof(FeatureGap.SuggestedTechStack), "suggested_tech_stack" },
        { typeof(FeatureGap), nameof(FeatureGap.ActionableImplementationPlan), "actionable_implementation_plan" },
        { typeof(FeatureGap), nameof(FeatureGap.IdentifiedAt), "identified_at" },
    };

    [Theory]
    [MemberData(nameof(SnakeCaseTables))]
    public void Entities_map_to_snake_case_tables(Type entityType, string expectedTableName)
    {
        using var context = CreateContext();
        Assert.Equal(expectedTableName, context.Model.FindEntityType(entityType)!.GetTableName());
    }

    [Theory]
    [MemberData(nameof(SnakeCaseColumns))]
    public void Properties_map_to_snake_case_columns(
        Type entityType,
        string propertyName,
        string expectedColumnName)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(entityType)!;
        var tableIdentifier = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        Assert.Equal(expectedColumnName, entity.FindProperty(propertyName)!.GetColumnName(tableIdentifier));
    }

    [Fact]
    public void Review_embedding_maps_to_nullable_vector_3072()
    {
        using var context = CreateContext();
        var embedding = context.Model.FindEntityType(typeof(Review))!.FindProperty(nameof(Review.Embedding))!;

        Assert.Equal("vector(3072)", embedding.GetColumnType());
        Assert.True(embedding.IsNullable);
        Assert.IsType<ValueConverter>(embedding.GetValueConverter(), exactMatch: false);
    }

    [Fact]
    public void Review_has_unique_composite_idempotency_index()
    {
        using var context = CreateContext();
        var review = context.Model.FindEntityType(typeof(Review))!;
        var tableIdentifier = StoreObjectIdentifier.Table(review.GetTableName()!, review.GetSchema());

        var index = Assert.Single(
            review.GetIndexes(),
            candidate => candidate.GetDatabaseName(tableIdentifier)
                == "ux_review_competitor_target_id_source_review_id");

        Assert.True(index.IsUnique);
        Assert.Equal(
            ["competitor_target_id", "source_review_id"],
            index.Properties.Select(property => property.GetColumnName(tableIdentifier)));
    }

    [Fact]
    public void FeatureGap_has_descending_ranked_gaps_index()
    {
        using var context = CreateContext();

        // IsDescending is a design-time-only configuration; the read-optimized
        // runtime model does not carry it.
        var model = context.GetService<IDesignTimeModel>().Model;
        var featureGap = model.FindEntityType(typeof(FeatureGap))!;
        var tableIdentifier = StoreObjectIdentifier.Table(featureGap.GetTableName()!, featureGap.GetSchema());

        var index = Assert.Single(
            featureGap.GetIndexes(),
            candidate => candidate.GetDatabaseName(tableIdentifier)
                == "ix_feature_gap_severity_score_mention_frequency");

        // EF stores "all columns descending" either as explicit per-column values or
        // as the parameterless IsDescending() encoding (empty array).
        Assert.True(
            index.IsDescending is [] or [true, true],
            "both ranked-gaps columns must be descending");
        Assert.Equal(
            ["severity_score", "mention_frequency"],
            index.Properties.Select(property => property.GetColumnName(tableIdentifier)));
    }

    [Fact]
    public void FeatureGap_severity_score_persists_the_wrapped_double()
    {
        using var context = CreateContext();
        var severityScore = context.Model.FindEntityType(typeof(FeatureGap))!
            .FindProperty(nameof(FeatureGap.SeverityScore))!;

        Assert.Equal("double precision", severityScore.GetColumnType());
        Assert.IsType<ValueConverter>(severityScore.GetValueConverter(), exactMatch: false);
    }

    [Fact]
    public void Model_enables_the_pgvector_extension()
    {
        using var context = CreateContext();
        var createScript = context.Database.GenerateCreateScript()
            .Replace("\"", string.Empty, StringComparison.Ordinal);

        Assert.Contains("CREATE EXTENSION IF NOT EXISTS vector", createScript, StringComparison.Ordinal);
    }
}
