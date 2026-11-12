using GapMiner.Domain.ValueObjects;

namespace GapMiner.Domain.Entities;

/// <summary>
/// A recurring feature gap mined from competitor reviews, with an actionable
/// implementation plan. Immutable after creation.
/// </summary>
public sealed class FeatureGap
{
    /// <summary>Gets the aggregate identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Gets the tracked competitor the gap was identified against.</summary>
    public Guid CompetitorTargetId { get; init; }

    /// <summary>Gets the short gap title.</summary>
    public string Title { get; init; }

    /// <summary>Gets the detailed gap description.</summary>
    public string DetailedDescription { get; init; }

    /// <summary>Gets the severity; range-enforced by the <see cref="SeverityScore"/> factory.</summary>
    public SeverityScore SeverityScore { get; init; }

    /// <summary>Gets how often the gap was mentioned across reviews; never negative.</summary>
    public int MentionFrequency { get; init; }

    /// <summary>Gets the suggested technology stack for closing the gap.</summary>
    public string SuggestedTechStack { get; init; }

    /// <summary>Gets the actionable implementation plan for closing the gap.</summary>
    public string ActionableImplementationPlan { get; init; }

    /// <summary>Gets the UTC moment the gap was identified.</summary>
    public DateTimeOffset IdentifiedAt { get; init; }

    public FeatureGap(
        Guid id,
        Guid competitorTargetId,
        string title,
        string detailedDescription,
        SeverityScore severityScore,
        int mentionFrequency,
        string suggestedTechStack,
        string actionableImplementationPlan,
        DateTimeOffset identifiedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(mentionFrequency);

        Id = id;
        CompetitorTargetId = competitorTargetId;
        Title = title;
        DetailedDescription = detailedDescription;
        SeverityScore = severityScore;
        MentionFrequency = mentionFrequency;
        SuggestedTechStack = suggestedTechStack;
        ActionableImplementationPlan = actionableImplementationPlan;
        IdentifiedAt = identifiedAt;
    }
}
