using GapMiner.Domain.Entities;

namespace GapMiner.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistence contract for <see cref="CompetitorTarget"/> aggregates
/// (plan T-1.3). Every asynchronous method propagates a
/// <see cref="CancellationToken"/> so callers (and the API layer) can cancel
/// in-flight database work.
/// </summary>
public interface ICompetitorTargetRepository
{
    /// <summary>
    /// Persists a new competitor target.
    /// </summary>
    /// <param name="target">The target to insert; its <see cref="CompetitorTarget.Id"/> is client-assigned.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be canceled.</param>
    /// <returns>A task representing the save operation.</returns>
    Task AddAsync(CompetitorTarget target, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches a single competitor target by its identifier.
    /// </summary>
    /// <param name="id">The aggregate identifier.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be canceled.</param>
    /// <returns>The target, or <see langword="null"/> when no row matches.</returns>
    Task<CompetitorTarget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches every tracked competitor target, ordered by
    /// <see cref="CompetitorTarget.CreatedAt"/> then <see cref="CompetitorTarget.Name"/>
    /// for a deterministic listing order.
    /// </summary>
    /// <param name="cancellationToken">Propagates notification that the operation should be canceled.</param>
    /// <returns>All targets in listing order.</returns>
    Task<IReadOnlyList<CompetitorTarget>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a target is already tracked for the given marketplace URL —
    /// the guard the ingestion path uses to avoid tracking the same listing twice.
    /// </summary>
    /// <param name="marketplaceUrl">The marketplace listing URL to look for.</param>
    /// <param name="cancellationToken">Propagates notification that the operation should be canceled.</param>
    /// <returns><see langword="true"/> when a target with that URL exists.</returns>
    Task<bool> ExistsByUrlAsync(Uri marketplaceUrl, CancellationToken cancellationToken = default);
}
