namespace GapMiner.Infrastructure.Persistence.Repositories;

/// <summary>
/// The ranking applied by
/// <see cref="IFeatureGapRepository.GetRankedAsync"/>. The two members mirror the
/// API's <c>sortBy=severity|frequency</c> query parameter (plan T-3.5).
/// </summary>
public enum FeatureGapSortBy
{
    /// <summary>
    /// Most severe first: <c>severity_score DESC</c>, ties broken by
    /// <c>mention_frequency DESC</c> — exactly the order the
    /// <c>ix_feature_gap_severity_score_mention_frequency</c> index backs.
    /// </summary>
    Severity = 0,

    /// <summary>
    /// Most mentioned first: <c>mention_frequency DESC</c>, ties broken by
    /// <c>severity_score DESC</c>.
    /// </summary>
    Frequency = 1,
}
