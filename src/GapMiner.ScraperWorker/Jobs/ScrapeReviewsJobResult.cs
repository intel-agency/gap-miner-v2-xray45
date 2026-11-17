namespace GapMiner.ScraperWorker.Jobs;

/// <summary>
/// The terminal status of one <c>ScrapeReviewsJob</c> execution.
/// </summary>
public enum ScrapeReviewsJobStatus
{
    /// <summary>The full lifecycle (steps 2–9) completed for the command.</summary>
    Succeeded,

    /// <summary>
    /// A lifecycle step failed; the structured failure log event carrying
    /// <c>JobId</c>, <c>TargetId</c> and <c>FailureReason</c> was emitted
    /// before this value was produced.
    /// </summary>
    Failed,
}

/// <summary>
/// The outcome of one <c>ScrapeReviewsJob</c> execution (plan T-2.3): the
/// status, the identifiers every structured log event carried, the
/// machine-readable <see cref="FailureReason"/> (null on success) and the
/// item counts from dataset download through mapping to persistence.
/// </summary>
/// <param name="Status">The terminal status.</param>
/// <param name="JobId">
/// The execution's own identifier — generated per run and stamped on every
/// log event, so a failure's log line, its dashboard entry and the outcome
/// correlate without reaching into the scheduler's storage.
/// </param>
/// <param name="TargetId">The scraped <see cref="Domain.Entities.CompetitorTarget"/> id.</param>
/// <param name="FailureReason">
/// A stable, machine-readable failure reason (<c>TargetNotFound</c>,
/// <c>RunTimedOut</c>, …) with detail appended, or <see langword="null"/> on
/// success.
/// </param>
/// <param name="ItemsDownloaded">Dataset items the run produced.</param>
/// <param name="ReviewsMapped">Items that mapped to <see cref="Domain.Entities.Review"/> entities.</param>
/// <param name="ItemsFiltered">Mapped reviews outside the pipeline's stars 1–3 window, dropped.</param>
/// <param name="ItemsInserted">Rows the idempotent bulk insert actually wrote.</param>
/// <param name="ItemsSkipped">Dataset items the mapper could not map losslessly.</param>
public sealed record ScrapeReviewsJobResult(
    ScrapeReviewsJobStatus Status,
    Guid JobId,
    Guid TargetId,
    string? FailureReason,
    int ItemsDownloaded,
    int ReviewsMapped,
    int ItemsFiltered,
    int ItemsInserted,
    int ItemsSkipped);
