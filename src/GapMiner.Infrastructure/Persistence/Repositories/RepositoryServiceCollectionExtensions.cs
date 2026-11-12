using GapMiner.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace GapMiner.Infrastructure.Persistence.Repositories;

/// <summary>
/// Registers the Story 2.3 repository implementations with the service
/// collection. Call alongside
/// <c>AddDbContext&lt;GapMinerDbContext&gt;(...)</c>; every repository is scoped to
/// share the ambient <see cref="GapMinerDbContext"/>.
/// </summary>
public static class RepositoryServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CompetitorTargetRepository"/>,
    /// <see cref="ReviewRepository"/> and <see cref="FeatureGapRepository"/>
    /// behind their interfaces, one scoped instance per request.
    /// </summary>
    /// <param name="services">The collection to register the repositories with.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddGapMinerRepositories(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICompetitorTargetRepository, CompetitorTargetRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IFeatureGapRepository, FeatureGapRepository>();

        return services;
    }
}
