using GapMiner.Domain.Entities;
using GapMiner.Domain.ValueObjects;
using Xunit;

namespace GapMiner.Domain.Tests;

public class FeatureGapTests
{
    private static FeatureGap CreateFeatureGap(
        SeverityScore? severityScore = null,
        int mentionFrequency = 3) =>
        new(
            id: Guid.NewGuid(),
            competitorTargetId: Guid.NewGuid(),
            title: "Bulk CSV export",
            detailedDescription: "Reviewers repeatedly ask for bulk CSV export of results.",
            severityScore: severityScore ?? SeverityScore.Create(7.5),
            mentionFrequency: mentionFrequency,
            suggestedTechStack: "ASP.NET Core minimal API + CsvHelper",
            actionableImplementationPlan: "Add /export endpoint streaming CsvHelper rows.",
            identifiedAt: new DateTimeOffset(2026, 11, 12, 11, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Constructor_AssignsAllProperties()
    {
        var id = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var severity = SeverityScore.Create(9.1);
        var identifiedAt = new DateTimeOffset(2026, 11, 12, 11, 0, 0, TimeSpan.Zero);

        var gap = new FeatureGap(
            id: id,
            competitorTargetId: targetId,
            title: "Dark mode",
            detailedDescription: "Missing dark mode UI.",
            severityScore: severity,
            mentionFrequency: 12,
            suggestedTechStack: "Blazor CSS isolation",
            actionableImplementationPlan: "Ship a dark theme toggled via prefers-color-scheme.",
            identifiedAt: identifiedAt);

        Assert.Equal(id, gap.Id);
        Assert.Equal(targetId, gap.CompetitorTargetId);
        Assert.Equal("Dark mode", gap.Title);
        Assert.Equal("Missing dark mode UI.", gap.DetailedDescription);
        Assert.Equal(severity, gap.SeverityScore);
        Assert.Equal(9.1, gap.SeverityScore.Value);
        Assert.Equal(12, gap.MentionFrequency);
        Assert.Equal("Blazor CSS isolation", gap.SuggestedTechStack);
        Assert.Equal("Ship a dark theme toggled via prefers-color-scheme.", gap.ActionableImplementationPlan);
        Assert.Equal(identifiedAt, gap.IdentifiedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(250)]
    public void Constructor_AcceptsNonNegativeMentionFrequency(int mentionFrequency)
    {
        var gap = CreateFeatureGap(mentionFrequency: mentionFrequency);

        Assert.Equal(mentionFrequency, gap.MentionFrequency);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_RejectsNegativeMentionFrequency(int mentionFrequency)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateFeatureGap(mentionFrequency: mentionFrequency));
    }

    [Fact]
    public void SeverityScore_CarriesRangeEnforcedValue()
    {
        var gap = CreateFeatureGap(severityScore: SeverityScore.Create(SeverityScore.Minimum));

        Assert.Equal(SeverityScore.Minimum, gap.SeverityScore.Value);
    }
}
