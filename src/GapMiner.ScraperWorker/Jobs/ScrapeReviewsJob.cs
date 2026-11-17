using System.Diagnostics;
using System.Text.RegularExpressions;
using GapMiner.Domain.Entities;
using GapMiner.Domain.Enums;
using GapMiner.Infrastructure.Persistence.Repositories;
using GapMiner.Infrastructure.Queueing;
using GapMiner.Infrastructure.Scraping;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace GapMiner.ScraperWorker.Jobs;

/// <summary>
/// The scrape pipeline's background job (plan T-2.3): dequeues a
/// <see cref="ScrapeTargetCommand"/> from the Redis scrape queue, resolves the
/// marketplace's actor through the registry, triggers an asynchronous Apify
/// run for the target's 1-to-3-star reviews, polls the run to a terminal
/// status within the run timeout, downloads the run's dataset items, maps them
/// to <see cref="Review"/> entities through the registry field mapping,
/// bulk-inserts them idempotently, enqueues the follow-up
/// <see cref="AnalyzeReviewsCommand"/> and stamps
/// <see cref="CompetitorTarget.LastScrapedAt"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hangfire registration.</b> The recurring job
/// <c>scrape-reviews</c> (cron <see cref="RecurrenceCron"/>, see
/// Program.cs) runs <see cref="RunPendingAsync"/> — lifecycle step 1. Each
/// tick dequeues at most one command (the destructive LPOP of the Redis queue
/// guarantees a command is never processed twice concurrently) and executes
/// the remaining lifecycle for it. <see cref="AutomaticRetryAttribute"/>
/// attempts are zero by design: the tick itself is idempotent, the next tick
/// is the retry, and Hangfire's default retry loop must never re-trigger paid
/// actor runs.
/// </para>
/// <para>
/// <b>Failure contract.</b> Every failure path returns a
/// <see cref="ScrapeReviewsJobResult"/> with a machine-readable
/// <see cref="ScrapeReviewsJobResult.FailureReason"/> after emitting one
/// structured error log event carrying <c>JobId</c>, <c>TargetId</c> and
/// <c>FailureReason</c> (the story's logging acceptance criterion) — business
/// failures never throw out of <see cref="ExecuteAsync"/>. Only cancellation
/// (host shutdown) propagates: the aborted tick is recorded by the scheduler,
/// and re-running the same command later is safe.
/// </para>
/// <para>
/// <b>Re-run safety.</b> Idempotency sits in the persistence layer: the
/// review bulk insert ignores rows whose (target, source review id) pair
/// already exists (Story 2.2's unique composite), including duplicates inside
/// one batch — re-running the same command re-scrapes but never duplicates
/// rows. The command is, however, consumed destructively: a crash between
/// dequeue and completion loses that command (Epic 2's queue semantics —
/// dead-lettering is consumer policy); recovery is re-enqueuing it, which is
/// safe.
/// </para>
/// <para>
/// <b>The stars 1–3 filter</b> is applied twice by design: server-side where
/// the actor's input schema filters by stars (Chrome Web Store's inclusive
/// <c>minRating</c>/<c>maxRating</c>), and always client-side on the mapped
/// <see cref="Review.StarRating"/> before persistence. The G2 actor's
/// <c>minRating</c>/<c>maxRating</c> inputs are NPS (0–10), not stars — the
/// run input deliberately leaves them unset so a stars-1–3 intent can never be
/// mis-sent as an NPS bound.
/// </para>
/// </remarks>
public sealed class ScrapeReviewsJob
{
    /// <summary>
    /// The run-status poll interval (lifecycle step 4's "every 15s"): passed
    /// to the status endpoint's <c>waitForFinish</c> hold, so the server holds
    /// each poll for up to 15 seconds and answers early on terminal status.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The maximum the status endpoint's <c>waitForFinish</c> hold accepts
    /// (documented cap of the Apify run-status endpoint).
    /// </summary>
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The maximum wall-clock time a run may take before the job gives up on
    /// it (lifecycle step 4's "60-minute max timeout"): the poll loop's
    /// deadline, after which the job fails with <c>RunTimedOut</c> and
    /// re-running the command remains safe.
    /// </summary>
    public static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromMinutes(60);

    /// <summary>
    /// The recurring-job cron schedule the composition root registers this
    /// job on: every minute, the smallest granularity Hangfire's recurring
    /// scheduler guarantees. Each tick takes at most one command, so queue
    /// concurrency is bounded by the Hangfire server's worker pool rather
    /// than by the tick cadence.
    /// </summary>
    public const string RecurrenceCron = "* * * * *";

    /// <summary>The scrape queue's own G2 input mode: scrape one product's reviews.</summary>
    private const string G2ProductReviewsMode = "product_reviews";

    /// <summary>
    /// Matches an Apple App Store listing path:
    /// <c>/&lt;country&gt;/app/&lt;slug&gt;/id&lt;app id&gt;</c> — the
    /// registry's canonical Apple input form needs the country code and the
    /// numeric app id.
    /// </summary>
    private static readonly Regex AppleAppPathRegex = new(
        "^/(?<country>[a-z]{2})/app/[^/]+/id(?<appId>[0-9]+)/?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IApifyClient apifyClient;
    private readonly MarketplaceActorRegistry registry;
    private readonly IReviewRepository reviewRepository;
    private readonly ICompetitorTargetRepository targetRepository;
    private readonly IJobQueue<ScrapeTargetCommand> scrapeQueue;
    private readonly IJobQueue<AnalyzeReviewsCommand> analysisQueue;
    private readonly ILogger<ScrapeReviewsJob> logger;
    private readonly DatasetReviewMapper mapper;
    private readonly TimeSpan pollInterval;
    private readonly TimeSpan runTimeout;

    /// <summary>
    /// Initializes the job over its collaborators with the documented default
    /// poll interval and run timeout.
    /// </summary>
    /// <param name="apifyClient">The typed Apify v2 client.</param>
    /// <param name="registry">The marketplace actor registry (scrapes resolve, never introspect).</param>
    /// <param name="reviewRepository">The review persistence layer.</param>
    /// <param name="targetRepository">The competitor-target persistence layer.</param>
    /// <param name="scrapeQueue">The scrape command queue.</param>
    /// <param name="analysisQueue">The analysis command queue.</param>
    /// <param name="logger">The job's logger.</param>
    /// <param name="mapper">The registry-driven dataset-to-review mapper.</param>
    /// <param name="pollInterval">
    /// The run-status poll interval; defaults to <see cref="DefaultPollInterval"/>.
    /// </param>
    /// <param name="runTimeout">
    /// The maximum run duration; defaults to <see cref="DefaultRunTimeout"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="pollInterval"/> or <paramref name="runTimeout"/> is not
    /// positive.
    /// </exception>
    public ScrapeReviewsJob(
        IApifyClient apifyClient,
        MarketplaceActorRegistry registry,
        IReviewRepository reviewRepository,
        ICompetitorTargetRepository targetRepository,
        IJobQueue<ScrapeTargetCommand> scrapeQueue,
        IJobQueue<AnalyzeReviewsCommand> analysisQueue,
        ILogger<ScrapeReviewsJob> logger,
        DatasetReviewMapper mapper,
        TimeSpan? pollInterval = null,
        TimeSpan? runTimeout = null)
    {
        this.apifyClient = apifyClient ?? throw new ArgumentNullException(nameof(apifyClient));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.reviewRepository = reviewRepository ?? throw new ArgumentNullException(nameof(reviewRepository));
        this.targetRepository = targetRepository ?? throw new ArgumentNullException(nameof(targetRepository));
        this.scrapeQueue = scrapeQueue ?? throw new ArgumentNullException(nameof(scrapeQueue));
        this.analysisQueue = analysisQueue ?? throw new ArgumentNullException(nameof(analysisQueue));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));

        this.pollInterval = pollInterval ?? DefaultPollInterval;
        this.runTimeout = runTimeout ?? DefaultRunTimeout;
        if (this.pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "The poll interval must be positive.");
        }

        if (this.pollInterval > MaxPollInterval)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                $"The poll interval must not exceed the status endpoint's waitForFinish cap ({MaxPollInterval.TotalSeconds:0}s).");
        }

        if (this.runTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(runTimeout), "The run timeout must be positive.");
        }
    }

    /// <summary>
    /// Lifecycle step 1 — the Hangfire entry point. Dequeues at most one
    /// pending <see cref="ScrapeTargetCommand"/> and, when the queue held one,
    /// executes the rest of the lifecycle for it. An empty queue returns
    /// silently; the scheduler re-runs this on the next tick.
    /// </summary>
    /// <param name="cancellationToken">
    /// The scheduler's shutdown token, propagated through the lifecycle (R4).
    /// Hangfire replaces the serialized <c>CancellationToken.None</c>
    /// placeholder with its own shutdown-aware token just before performing
    /// the job (<c>CoreBackgroundJobPerformer</c> substitutes every
    /// <see cref="CancellationToken"/> parameter), so the recurring
    /// registration in Program.cs does wire real shutdown into the lifecycle.
    /// </param>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunPendingAsync(CancellationToken cancellationToken = default)
    {
        var command = await scrapeQueue.DequeueAsync(cancellationToken).ConfigureAwait(false);
        if (command is null)
        {
            return;
        }

        await ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes lifecycle steps 2–9 for one command: resolve, run, poll,
    /// download, map, insert, enqueue analysis, stamp
    /// <see cref="CompetitorTarget.LastScrapedAt"/>. Public so the queue
    /// round-trip integration test (and any future re-scrape trigger) can
    /// drive the lifecycle for a command it already holds.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">Propagates cancellation (R4).</param>
    /// <returns>The execution outcome — failures return, never throw.</returns>
    public async Task<ScrapeReviewsJobResult> ExecuteAsync(
        ScrapeTargetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var jobId = Guid.NewGuid();
        var targetId = command.CompetitorTargetId;
        logger.LogInformation(
            "Scrape reviews job started. JobId: {JobId}, TargetId: {TargetId}", jobId, targetId);

        try
        {
            return await RunLifecycleAsync(command, jobId, targetId, cancellationToken).ConfigureAwait(false);
        }
        catch (ScrapeFailureException failure)
        {
            return Fail(jobId, targetId, failure.Reason, failure.InnerException);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown is not a pipeline failure: the aborted tick is
            // visible in the scheduler; the command can be safely re-enqueued.
            // The filter matters: an OperationCanceledException without a
            // canceled caller token (HttpClient.Timeout's TaskCanceledException)
            // is a failure and falls through to the structured failure path.
            throw;
        }
        catch (Exception unexpected)
        {
            return Fail(
                jobId,
                targetId,
                $"Unhandled: {unexpected.GetType().Name}: {unexpected.Message}",
                unexpected);
        }
    }

    private async Task<ScrapeReviewsJobResult> RunLifecycleAsync(
        ScrapeTargetCommand command,
        Guid jobId,
        Guid targetId,
        CancellationToken cancellationToken)
    {
        // Step 2: resolve the target and its marketplace actor. Resolve (not
        // TryGet) so an unverified actor entry fails fast, per the registry
        // contract.
        var target = await StepAsync(
            () => targetRepository.GetByIdAsync(targetId, cancellationToken),
            "TargetLoadFailed",
            cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            throw new ScrapeFailureException($"TargetNotFound: no competitor target {targetId} exists.");
        }

        MarketplaceActorEntry entry;
        try
        {
            entry = registry.Resolve(target.MarketplaceKind);
        }
        catch (MarketplaceNotSupportedException failure)
        {
            throw new ScrapeFailureException(
                $"MarketplaceNotSupported: {failure.Message}", failure);
        }

        // Step 3: trigger the asynchronous run with the target URL and the
        // stars 1-3 review filter where the actor's schema can express it.
        var runInput = BuildRunInput(target, entry);
        var start = await StepAsync(
            () => apifyClient.RunActorAsync(entry.ActorId, runInput, cancellationToken: cancellationToken),
            "RunStartFailed",
            cancellationToken).ConfigureAwait(false);
        var runId = start.Data?.Id;
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ScrapeFailureException("RunStartFailed: the response carried no run id.");
        }

        // Step 4: poll to a terminal status within the run timeout.
        var finishedRun = await WaitForRunAsync(runId, cancellationToken).ConfigureAwait(false);
        var datasetId = finishedRun.DefaultDatasetId;
        if (string.IsNullOrWhiteSpace(datasetId))
        {
            throw new ScrapeFailureException(
                $"RunSucceededWithoutDataset: run {runId} finished {finishedRun.Status} with no default dataset id.");
        }

        // Step 5: download the dataset items.
        var items = await StepAsync(
            () => apifyClient.GetDatasetItemsAsync(datasetId, clean: true, cancellationToken: cancellationToken),
            "DatasetDownloadFailed",
            cancellationToken).ConfigureAwait(false);

        // Step 6: map through the registry field mapping.
        var mapping = mapper.Map(entry, items, targetId);

        // The pipeline's stars 1-3 window, enforced client-side for every
        // marketplace regardless of actor-side filtering.
        var negativeReviews = mapping.Reviews.Where(review => review.StarRating <= 3).ToList();
        var filtered = mapping.Reviews.Count - negativeReviews.Count;

        // Step 7: idempotent bulk insert (duplicates ignored, in-batch included).
        var inserted = await StepAsync(
            () => reviewRepository.BulkInsertIgnoreDuplicatesAsync(negativeReviews, cancellationToken),
            "ReviewInsertFailed",
            cancellationToken).ConfigureAwait(false);

        // Step 8: hand the target to the analysis pipeline.
        await StepAsync(
            () => analysisQueue.EnqueueAsync(new AnalyzeReviewsCommand(targetId), cancellationToken),
            "AnalysisEnqueueFailed",
            cancellationToken).ConfigureAwait(false);

        // Step 9: stamp the target's last-scraped moment.
        await StepAsync(
            () => targetRepository.MarkScrapedAsync(targetId, DateTimeOffset.UtcNow, cancellationToken),
            "LastScrapedAtStampFailed",
            cancellationToken).ConfigureAwait(false);

        var result = new ScrapeReviewsJobResult(
            Status: ScrapeReviewsJobStatus.Succeeded,
            JobId: jobId,
            TargetId: targetId,
            FailureReason: null,
            ItemsDownloaded: items.Count,
            ReviewsMapped: mapping.Reviews.Count,
            ItemsFiltered: filtered,
            ItemsInserted: inserted,
            ItemsSkipped: mapping.SkippedItemCount);
        logger.LogInformation(
            "Scrape reviews job completed. JobId: {JobId}, TargetId: {TargetId}, ItemsDownloaded: {ItemsDownloaded}, "
            + "ReviewsMapped: {ReviewsMapped}, ItemsFiltered: {ItemsFiltered}, ItemsInserted: {ItemsInserted}, ItemsSkipped: {ItemsSkipped}",
            jobId, targetId, result.ItemsDownloaded, result.ReviewsMapped, result.ItemsFiltered,
            result.ItemsInserted, result.ItemsSkipped);
        return result;
    }

    /// <summary>
    /// Lifecycle step 4: polls the run's status, each poll held server-side
    /// for up to the poll interval, until a terminal status or the run
    /// timeout — whichever comes first.
    /// </summary>
    /// <param name="runId">The run to poll.</param>
    /// <param name="cancellationToken">Propagates cancellation (R4).</param>
    /// <returns>The succeeded run.</returns>
    /// <exception cref="ScrapeFailureException">
    /// The run finished unsuccessfully, its status could not be read, or the
    /// run timeout elapsed first.
    /// </exception>
    private async Task<ApifyActorRun> WaitForRunAsync(string runId, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await StepAsync(
                () => apifyClient.GetActorRunAsync(runId, pollInterval.TotalSeconds, cancellationToken),
                "RunStatusPollFailed",
                cancellationToken).ConfigureAwait(false);

            var run = response.Data;
            if (run is null || string.IsNullOrWhiteSpace(run.Status))
            {
                throw new ScrapeFailureException("RunStatusPollFailed: the response carried no run object.");
            }

            switch (run.Status)
            {
                case ApifyRunStatus.Succeeded:
                    return run;
                case ApifyRunStatus.Failed:
                case ApifyRunStatus.TimedOut:
                case ApifyRunStatus.Aborted:
                    throw new ScrapeFailureException(
                        $"Run{run.Status}: {run.StatusMessage ?? "no status message"}");
            }

            // READY/RUNNING/TIMING-OUT/ABORTING (or an undocumented status):
            // keep polling until the run timeout.
            if (Stopwatch.GetElapsedTime(startedAt) >= runTimeout)
            {
                throw new ScrapeFailureException(
                    $"RunTimedOut: run {runId} exceeded the {runTimeout} run timeout without a terminal "
                    + "status (deadline checked between polls).");
            }
        }
    }

    /// <summary>
    /// Builds the actor run input for the target — lifecycle step 3's "target
    /// URL and review filter (stars 1–3)" — in the registry entry's canonical
    /// input form (Story 3.2's live-store-verified
    /// <see cref="MarketplaceActorEntry.RequiredInputKeys"/>).
    /// </summary>
    /// <param name="target">The target being scraped.</param>
    /// <param name="entry">The resolved registry entry.</param>
    /// <returns>The actor input object for the run.</returns>
    /// <exception cref="ScrapeFailureException">
    /// The target's URL does not carry the marketplace's required input (the
    /// Apple form needs a parseable country code and app id).
    /// </exception>
    internal static Dictionary<string, object?> BuildRunInput(CompetitorTarget target, MarketplaceActorEntry entry)
    {
        var url = target.MarketplaceUrl.AbsoluteUri;

        switch (target.MarketplaceKind)
        {
            case MarketplaceKind.ShopifyAppStore:
                // appReviewUrl: the listing's review page URL (string).
                return new Dictionary<string, object?>
                {
                    ["appReviewUrl"] = url,
                };

            case MarketplaceKind.ChromeWebStore:
                // startUrls: { url } objects; minRating/maxRating are inclusive
                // star bounds — the one marketplace whose schema filters by
                // stars natively.
                return new Dictionary<string, object?>
                {
                    ["startUrls"] = new[] { new Dictionary<string, string> { ["url"] = url } },
                    ["minRating"] = 1,
                    ["maxRating"] = 3,
                };

            case MarketplaceKind.AppleAppStore:
                // appIds (numeric id strings) + country; Apple caps at 500
                // reviews per country.
                var match = AppleAppPathRegex.Match(target.MarketplaceUrl.AbsolutePath);
                if (!target.MarketplaceUrl.Host.Equals("apps.apple.com", StringComparison.OrdinalIgnoreCase)
                    || !match.Success)
                {
                    throw new ScrapeFailureException(
                        $"InvalidTargetUrl: {url} is not an apps.apple.com app listing URL.");
                }

                return new Dictionary<string, object?>
                {
                    ["appIds"] = new[] { match.Groups["appId"].Value },
                    ["country"] = match.Groups["country"].Value,
                };

            case MarketplaceKind.G2:
                // mode + productUrls. minRating/maxRating are NPS 0-10, not
                // stars — deliberately unset (the client-side 1-3 filter
                // enforces the review window instead).
                return new Dictionary<string, object?>
                {
                    ["mode"] = G2ProductReviewsMode,
                    ["productUrls"] = new[] { url },
                };

            default:
                throw new ScrapeFailureException(
                    $"MarketplaceNotSupported: {target.MarketplaceKind} has no run input form.");
        }
    }

    /// <summary>
    /// Runs one lifecycle step, folding any failure of the step (other than
    /// cancellation, which propagates) into a
    /// <see cref="ScrapeFailureException"/> whose reason is prefixed with the
    /// step name — from the job's perspective, whatever went wrong inside a
    /// step, that step failed. Only an <see cref="OperationCanceledException"/>
    /// thrown while the lifecycle's own token is actually canceled escapes the
    /// folding: a <see cref="TaskCanceledException"/> from a source other than
    /// the caller's token (the <c>HttpClient.Timeout</c> case) is a step
    /// failure like any other and must reach the structured failure log, not
    /// masquerade as host shutdown.
    /// </summary>
    private static async Task<T> StepAsync<T>(
        Func<Task<T>> step,
        string failurePrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            return await step().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            throw new ScrapeFailureException($"{failurePrefix}: {Detail(failure)}", failure);
        }
    }

    /// <summary>The task-returning counterpart for steps that produce no value.</summary>
    private static async Task StepAsync(
        Func<Task> step,
        string failurePrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            await step().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            throw new ScrapeFailureException($"{failurePrefix}: {Detail(failure)}", failure);
        }
    }

    private static string Detail(Exception failure) => $"{failure.GetType().Name}: {failure.Message}";

    private ScrapeReviewsJobResult Fail(
        Guid jobId,
        Guid targetId,
        string failureReason,
        Exception? failure = null)
    {
        logger.LogError(
            failure,
            "Scrape reviews job failed. JobId: {JobId}, TargetId: {TargetId}, FailureReason: {FailureReason}",
            jobId, targetId, failureReason);
        return new ScrapeReviewsJobResult(
            Status: ScrapeReviewsJobStatus.Failed,
            JobId: jobId,
            TargetId: targetId,
            FailureReason: failureReason,
            ItemsDownloaded: 0,
            ReviewsMapped: 0,
            ItemsFiltered: 0,
            ItemsInserted: 0,
            ItemsSkipped: 0);
    }

    /// <summary>
    /// Carries a lifecycle step failure's machine-readable reason up to
    /// <see cref="ExecuteAsync"/>, which turns it into the structured
    /// failure log event and the failed result. Private by design: callers
    /// outside the job observe failures through the result.
    /// </summary>
    /// <param name="Reason">The failure reason, prefixed with the step name.</param>
    /// <param name="inner">The underlying exception, when there was one.</param>
    private sealed class ScrapeFailureException(string Reason, Exception? inner = null)
        : Exception(Reason, inner)
    {
        public string Reason { get; } = Reason;
    }
}
