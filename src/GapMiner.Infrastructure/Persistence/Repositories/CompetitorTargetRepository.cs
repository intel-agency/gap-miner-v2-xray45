using GapMiner.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GapMiner.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core-backed <see cref="ICompetitorTargetRepository"/> over
/// <see cref="GapMinerDbContext"/>. Reads are no-tracked: targets are immutable
/// after creation, so no query result is ever re-saved.
/// </summary>
public sealed class CompetitorTargetRepository(GapMinerDbContext context) : ICompetitorTargetRepository
{
    /// <inheritdoc />
    public async Task AddAsync(CompetitorTarget target, CancellationToken cancellationToken = default)
    {
        await context.CompetitorTargets.AddAsync(target, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<CompetitorTarget?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.CompetitorTargets
            .AsNoTracking()
            .FirstOrDefaultAsync(target => target.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompetitorTarget>> GetAllAsync(CancellationToken cancellationToken = default)
        => await context.CompetitorTargets
            .AsNoTracking()
            .OrderBy(target => target.CreatedAt)
            .ThenBy(target => target.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsByUrlAsync(Uri marketplaceUrl, CancellationToken cancellationToken = default)
        => context.CompetitorTargets
            .AsNoTracking()
            .AnyAsync(target => target.MarketplaceUrl == marketplaceUrl, cancellationToken);
}
