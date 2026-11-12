using GapMiner.Domain.Entities;

namespace GapMiner.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence contract for <see cref="FeatureGap"/> rows
/// (plan T-1.3), including the ranked query that backs the dashboard's
/// "most severe / most mentioned" views. Every asynchronous method propagates
/// a <see cref="CancellationToken"/>.
/// </summary>
public interface IFeatureGapRepository
{
    /// <summary>
    /// Persists a new feature gap mined by the analysis pipeline.
    /// </summary>
    /// <param name="gap">The gap to insert; its <see cref="FeatureGap.Id"/> is client-assigned.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be canceled.</param>
    /// <returns>A task representing the save operation.</returns>
    Task AddAsync(FeatureGap gap, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a page of all feature gaps in the requested ranking. The sort options
    /// mirror the API's <c>sortBy=severity|frequency</c> query parameter (plan T-3.5);
    /// <see cref="FeatureGapSortBy.Severity"/> follows the
    /// <c>ix_feature_gap_severity_score_mention_frequency</c> index order exactly.
    /// </summary>
    /// <param name="sortBy">The ranking to apply.</param>
    /// <param name="skip">The number of ranked rows to skip; must not be negative.</param>
    /// <param name="take">The page size; must be positive.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be canceled.</param>
    /// <returns>The requested page of gaps in ranked order.</returns>
    Task<IReadOnlyList<FeatureGap>> GetRankedAsync(
        FeatureGapSortBy sortBy,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches every feature gap identified against one target, ordered by
    /// <see cref="FeatureGap.IdentifiedAt"/> then <see cref="FeatureGap.Id"/> for a
    /// deterministic order.
    /// </summary>
    /// <param name="targetId">The competitor target to fetch gaps for.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be canceled.</param>
    /// <returns>All gaps of the target in identification order.</returns>
    Task<IReadOnlyList<FeatureGap>> GetByTargetIdAsync(Guid targetId, CancellationToken cancellationToken = default);
}
