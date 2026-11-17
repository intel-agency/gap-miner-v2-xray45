namespace GapMiner.Infrastructure.Queueing;

/// <summary>
/// The command that triggers a review scrape of one competitor target (plan
/// T-2.3): enqueued onto <see cref="GapMinerQueueKeys.ScrapePending"/> by the
/// ingestion path, dequeued by the ScraperWorker's
/// <c>ScrapeReviewsJob</c>. A plain JSON record — the queue serializes it
/// camelCase, so the wire form is <c>{ "competitorTargetId": "…" }</c>.
/// </summary>
/// <param name="CompetitorTargetId">
/// The <see cref="Domain.Entities.CompetitorTarget"/> to scrape.
/// </param>
public sealed record ScrapeTargetCommand(Guid CompetitorTargetId);
