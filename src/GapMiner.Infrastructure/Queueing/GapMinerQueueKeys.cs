namespace GapMiner.Infrastructure.Queueing;

/// <summary>
/// The canonical Redis list keys of the GapMiner pipeline queues, following the
/// development plan's <c>gapminer:{domain}:{action}</c> convention (§6). One
/// source of truth for every producer and consumer — a typo'd key would
/// silently strand commands, so registrations and tests read these constants
/// instead of repeating string literals.
/// </summary>
public static class GapMinerQueueKeys
{
    /// <summary>
    /// The scrape pipeline's pending queue: carries
    /// <see cref="ScrapeTargetCommand"/> messages from the ingestion path to
    /// the ScraperWorker's scrape job.
    /// </summary>
    public const string ScrapePending = "gapminer:scrape:pending";

    /// <summary>
    /// The analysis pipeline's pending queue: carries
    /// <see cref="AnalyzeReviewsCommand"/> messages from the scrape job to the
    /// AIWorker's analysis jobs.
    /// </summary>
    public const string AnalysisPending = "gapminer:analysis:pending";
}
