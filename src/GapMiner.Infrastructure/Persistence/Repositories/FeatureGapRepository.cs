using GapMiner.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GapMiner.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core-backed <see cref="IFeatureGapRepository"/> over
/// <see cref="GapMinerDbContext"/>. The severity ranking follows the
/// <c>ix_feature_gap_severity_score_mention_frequency</c> index order exactly
/// (severity DESC, mention frequency DESC).
/// </summary>
public sealed class FeatureGapRepository(GapMinerDbContext context) : IFeatureGapRepository
{
    /// <inheritdoc />
    public async Task AddAsync(FeatureGap gap, CancellationToken cancellationToken = default)
    {
        await context.FeatureGaps.AddAsync(gap, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeatureGap>> GetRankedAsync(
        FeatureGapSortBy sortBy,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(skip, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        var ranked = sortBy switch
        {
            FeatureGapSortBy.Severity => context.FeatureGaps
                .AsNoTracking()
                .OrderByDescending(gap => gap.SeverityScore)
                .ThenByDescending(gap => gap.MentionFrequency),
            FeatureGapSortBy.Frequency => context.FeatureGaps
                .AsNoTracking()
                .OrderByDescending(gap => gap.MentionFrequency)
                .ThenByDescending(gap => gap.SeverityScore),
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy), sortBy, "Unknown feature-gap ranking."),
        };

        return await ranked
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeatureGap>> GetByTargetIdAsync(
        Guid targetId,
        CancellationToken cancellationToken = default)
        => await context.FeatureGaps
            .AsNoTracking()
            .Where(gap => gap.CompetitorTargetId == targetId)
            .OrderBy(gap => gap.IdentifiedAt)
            .ThenBy(gap => gap.Id)
            .ToListAsync(cancellationToken);
}
