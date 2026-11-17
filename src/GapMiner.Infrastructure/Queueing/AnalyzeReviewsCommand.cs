namespace GapMiner.Infrastructure.Queueing;

/// <summary>
/// The command that triggers AI analysis of one competitor target's reviews
/// (plan T-2.3, lifecycle step 8): enqueued onto
/// <see cref="GapMinerQueueKeys.AnalysisPending"/> by the scrape job once a
/// scrape's reviews are persisted, dequeued by the AIWorker's analysis jobs
/// (Epic 4). A plain JSON record — the queue serializes it camelCase, so the
/// wire form is <c>{ "competitorTargetId": "…" }</c>.
/// </summary>
/// <param name="CompetitorTargetId">
/// The <see cref="Domain.Entities.CompetitorTarget"/> whose reviews are ready
/// to analyze.
/// </param>
public sealed record AnalyzeReviewsCommand(Guid CompetitorTargetId);
