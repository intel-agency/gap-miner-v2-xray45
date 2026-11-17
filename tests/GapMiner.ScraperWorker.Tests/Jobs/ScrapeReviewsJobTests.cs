using System.Text.Json;
using GapMiner.Domain.Entities;
using GapMiner.Domain.Enums;
using GapMiner.Infrastructure.Persistence.Repositories;
using GapMiner.Infrastructure.Queueing;
using GapMiner.Infrastructure.Scraping;
using GapMiner.ScraperWorker.Jobs;
using GapMiner.ScraperWorker.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace GapMiner.ScraperWorker.Tests.Jobs;

/// <summary>
/// Story 3.3's lifecycle acceptance: <see cref="ScrapeReviewsJob"/> runs the
/// nine steps against doubled collaborators — the Apify client, the
/// repositories and the analysis queue are NSubstitute doubles; the registry
/// and the mapper are the real ones. These tests pin the run-input forms per
/// marketplace (live-store-verified in Story 3.2), the stars 1–3 filter, the
/// poll bounds, the follow-up command and the structured failure log.
/// </summary>
public sealed class ScrapeReviewsJobTests
{
    private static readonly Guid TargetId = Guid.NewGuid();

    private readonly IApifyClient apifyClient = Substitute.For<IApifyClient>();
    private readonly IReviewRepository reviewRepository = Substitute.For<IReviewRepository>();
    private readonly ICompetitorTargetRepository targetRepository = Substitute.For<ICompetitorTargetRepository>();
    private readonly IJobQueue<ScrapeTargetCommand> scrapeQueue = Substitute.For<IJobQueue<ScrapeTargetCommand>>();
    private readonly IJobQueue<AnalyzeReviewsCommand> analysisQueue = Substitute.For<IJobQueue<AnalyzeReviewsCommand>>();
    private readonly SpyLogger<ScrapeReviewsJob> logger = new();

    private ScrapeReviewsJob CreateJob(TimeSpan? pollInterval = null, TimeSpan? runTimeout = null)
        => new(
            apifyClient,
            MarketplaceActorRegistry.Default,
            reviewRepository,
            targetRepository,
            scrapeQueue,
            analysisQueue,
            logger,
            new DatasetReviewMapper(),
            pollInterval,
            runTimeout);

    private CompetitorTarget Target(MarketplaceKind kind, string url)
        => new(
            Guid.NewGuid(),
            "Competitor",
            kind,
            new Uri(url),
            new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));

    private static ApifyDatasetItem Item(string json)
        => JsonSerializer.Deserialize<ApifyDatasetItem>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    /// <summary>
    /// Scripts the Apify run for one target: run start, one transitional poll,
    /// the succeeded poll, and the dataset download.
    /// </summary>
    private void ScriptSuccessfulRun(string runId, string datasetId, string itemsJson)
    {
        apifyClient.RunActorAsync(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApifyRunResponse
            {
                Data = new ApifyActorRun { Id = runId, Status = ApifyRunStatus.Running },
            });
        apifyClient.GetActorRunAsync(runId, Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(
                new ApifyRunResponse
                {
                    Data = new ApifyActorRun { Id = runId, Status = ApifyRunStatus.Running },
                },
                new ApifyRunResponse
                {
                    Data = new ApifyActorRun
                    {
                        Id = runId,
                        Status = ApifyRunStatus.Succeeded,
                        DefaultDatasetId = datasetId,
                    },
                });
        apifyClient.GetDatasetItemsAsync(
                datasetId, Arg.Any<bool?>(), Arg.Any<long?>(), Arg.Any<long?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlyList<ApifyDatasetItem>)JsonSerializer.Deserialize<List<ApifyDatasetItem>>(
                itemsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
    }

    private void ScriptTarget(CompetitorTarget target)
        => targetRepository
            .GetByIdAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(target);

    // The happy path, once per marketplace input form.

    [Fact]
    public async Task Executes_the_full_lifecycle_for_a_chrome_web_store_target()
    {
        var target = Target(
            MarketplaceKind.ChromeWebStore,
            "https://chromewebstore.google.com/detail/dark-reader/eimadpbcbfnmbkopoojfekhnkhdbieeh/reviews");
        ScriptTarget(target);
        ScriptSuccessfulRun(
            "run-1",
            "ds-1",
            """
            [
             {"reviewId":"r1","reviewBody":"Drains the battery","rating":2,"reviewerName":"Kim","reviewDate":"2026-10-04T00:00:00Z"},
             {"reviewId":"r2","reviewBody":"Love it","rating":5,"reviewerName":"Sam","reviewDate":"2026-10-05T00:00:00Z"},
             {"reviewId":"r3","rating":1,"reviewerName":"No Body","reviewDate":"2026-10-06T00:00:00Z"}
            ]
            """);
        reviewRepository
            .BulkInsertIgnoreDuplicatesAsync(Arg.Any<IReadOnlyList<Review>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<IReadOnlyList<Review>>(0).Count);

        var result = await CreateJob().ExecuteAsync(new ScrapeTargetCommand(target.Id));

        Assert.Equal(ScrapeReviewsJobStatus.Succeeded, result.Status);
        Assert.Equal(target.Id, result.TargetId);
        Assert.Null(result.FailureReason);
        Assert.Equal(3, result.ItemsDownloaded);
        Assert.Equal(2, result.ReviewsMapped);
        Assert.Equal(1, result.ItemsFiltered);
        Assert.Equal(1, result.ItemsSkipped);
        Assert.Equal(1, result.ItemsInserted);

        // Step 3: the CWS input form — { url } objects plus the native stars
        // 1-3 bounds.
        await apifyClient.Received(1).RunActorAsync(
            "automation-lab~chrome-web-store-reviews-scraper",
            Arg.Is<Dictionary<string, object?>>(dictionary =>
                dictionary.ContainsKey("startUrls")
                && dictionary["minRating"]!.Equals(1)
                && dictionary["maxRating"]!.Equals(3)),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Step 4: polled with the documented interval.
        await apifyClient.Received(2).GetActorRunAsync("run-1", 15, Arg.Any<CancellationToken>());

        // Step 7: only the 1-3 star reviews persisted.
        var inserted = reviewRepository.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IReviewRepository.BulkInsertIgnoreDuplicatesAsync))
            .GetArguments()[0] as IReadOnlyList<Review>;
        Assert.NotNull(inserted);
        Assert.Equal(["r1"], inserted.Select(review => review.SourceReviewId).ToArray());
        Assert.All(inserted, review => Assert.InRange(review.StarRating, 1, 3));

        // Step 8: the follow-up analysis command for this target.
        await analysisQueue.Received(1).EnqueueAsync(
            new AnalyzeReviewsCommand(target.Id), Arg.Any<CancellationToken>());

        // Step 9: the last-scraped stamp.
        await targetRepository.Received(1).MarkScrapedAsync(
            target.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Builds_the_shopify_input_from_the_target_url()
    {
        var target = Target(
            MarketplaceKind.ShopifyAppStore,
            "https://apps.shopify.com/judgeme/reviews");
        ScriptTarget(target);
        ScriptSuccessfulRun("run-s", "ds-s", "[]");
        reviewRepository
            .BulkInsertIgnoreDuplicatesAsync(Arg.Any<IReadOnlyList<Review>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await CreateJob().ExecuteAsync(new ScrapeTargetCommand(target.Id));

        await apifyClient.Received(1).RunActorAsync(
            "powerai~shopify-app-reviews-scraper",
            Arg.Is<Dictionary<string, object?>>(dictionary =>
                ((string)dictionary["appReviewUrl"]!).EndsWith("/judgeme/reviews")
                && dictionary.Count == 1),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Builds_the_apple_input_from_the_country_and_app_id_in_the_url()
    {
        var target = Target(
            MarketplaceKind.AppleAppStore,
            "https://apps.apple.com/us/app/instagram/id389801252?see-all=1");
        ScriptTarget(target);
        ScriptSuccessfulRun("run-a", "ds-a", "[]");
        reviewRepository
            .BulkInsertIgnoreDuplicatesAsync(Arg.Any<IReadOnlyList<Review>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await CreateJob().ExecuteAsync(new ScrapeTargetCommand(target.Id));

        await apifyClient.Received(1).RunActorAsync(
            "thewolves~appstore-reviews-scraper",
            Arg.Is<Dictionary<string, object?>>(dictionary =>
                ((string[])dictionary["appIds"]!).SequenceEqual(new[] { "389801252" })
                && ((string)dictionary["country"]!).Equals("us")),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Builds_the_g2_input_without_any_rating_bound()
    {
        // G2's minRating/maxRating inputs are NPS 0-10, not stars — a stars
        // 1-3 intent must never be sent as an NPS bound.
        var target = Target(
            MarketplaceKind.G2,
            "https://www.g2.com/products/slack/reviews");
        ScriptTarget(target);
        ScriptSuccessfulRun("run-g", "ds-g", "[]");
        reviewRepository
            .BulkInsertIgnoreDuplicatesAsync(Arg.Any<IReadOnlyList<Review>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await CreateJob().ExecuteAsync(new ScrapeTargetCommand(target.Id));

        await apifyClient.Received(1).RunActorAsync(
            "automation-lab~g2-scraper",
            Arg.Is<Dictionary<string, object?>>(dictionary =>
                ((string)dictionary["mode"]!).Equals("product_reviews")
                && ((string[])dictionary["productUrls"]!).SequenceEqual(new[] { "https://www.g2.com/products/slack/reviews" })
                && !dictionary.ContainsKey("minRating")
                && !dictionary.ContainsKey("maxRating")),
            Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // Failure paths: each returns a failed result and one structured log event
    // carrying JobId, TargetId and FailureReason (the story's logging AC).

    [Fact]
    public async Task Fails_with_a_structured_event_when_the_target_is_missing()
    {
        targetRepository
            .GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((CompetitorTarget?)null);

        var result = await CreateJob().ExecuteAsync(new ScrapeTargetCommand(TargetId));

        Assert.Equal(ScrapeReviewsJobStatus.Failed, result.Status);
        Assert.StartsWith("TargetNotFound", result.FailureReason);
        await apifyClient.DidNotReceiveWithAnyArgs().RunActorAsync(
            default!, default!, default, default, default, default, default);

        var failure = logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        Assert.Equal(result.JobId.ToString(), failure.State["JobId"]!.ToString());
        Assert.Equal(TargetId.ToString(), failure.State["TargetId"]!.ToString());
        Assert.Equal(result.FailureReason, failure.State["FailureReason"]!.ToString());
    }

    [Fact]
    public async Task Fails_when_the_marketplace_actor_is_not_supported()
    {
        // A registry whose only entry is NeedsVerification: Resolve throws —
        // the scrape path's fail-fast contract.
        var unverified = new MarketplaceActorRegistry(new Dictionary<MarketplaceKind, MarketplaceActorEntry>
        {
            [MarketplaceKind.G2] = new MarketplaceActorEntry(
                MarketplaceKind.G2,
                "automation-lab~g2-scraper",
                SourceReviewIdFieldName: "reviewId",
                ReviewFieldName: "reviewText",
                StarRatingFieldName: "starRating",
                AuthorFieldName: "reviewerName",
                DateFieldName: "publishedAt",
                RequiredInputKeys: new[] { "mode", "productUrls" },
                MarketplaceActorEntryStatus.NeedsVerification,
                VerificationNote: "test: awaiting re-verification"),
        });
        var target = Target(MarketplaceKind.G2, "https://www.g2.com/products/slack/reviews");
        ScriptTarget(target);
        var job = new ScrapeReviewsJob(
            apifyClient, unverified, reviewRepository, targetRepository, scrapeQueue, analysisQueue,
            logger, new DatasetReviewMapper());

        var result = await job.ExecuteAsync(new ScrapeTargetCommand(target.Id));

        Assert.Equal(ScrapeReviewsJobStatus.Failed, result.Status);
        Assert.StartsWith("MarketplaceNotSupported", result.FailureReason);
        Assert.Contains("NeedsVerification", result.FailureReason);
        await apifyClient.DidNotReceiveWithAnyArgs().RunActorAsync(
            default!, default!, default, default, default, default, default);
    }

    [Fact]
    public async Task Fails_when_the_run_ends_in_a_terminal_non_succeeded_status()
    {
        var target = Target(
            MarketplaceKind.ShopifyAppStore,
            "https://apps.shopify.com/judgeme/reviews");
        ScriptTarget(target);
        apifyClient.RunActorAsync(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApifyRunResponse
            {
                Data = new ApifyActorRun { Id = "run-f", Status = ApifyRunStatus.Running },
            });
        apifyClient.GetActorRunAsync("run-f", Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new ApifyRunResponse
            {
                Data = new ApifyActorRun
                {
                    Id = "run-f",
                    Status = ApifyRunStatus.Failed,
                    StatusMessage = "Actor crashed",
                },
            });

        var result = await CreateJob().ExecuteAsync(new ScrapeTargetCommand(target.Id));

        Assert.Equal(ScrapeReviewsJobStatus.Failed, result.Status);
        Assert.StartsWith("RunFAILED", result.FailureReason);
        Assert.Contains("Actor crashed", result.FailureReason);
        await reviewRepository.DidNotReceiveWithAnyArgs().BulkInsertIgnoreDuplicatesAsync(
            default!, default);
    }

    [Fact]
    public async Task Fails_when_the_run_exceeds_the_run_timeout()
    {
        var target = Target(
            MarketplaceKind.ShopifyAppStore,
            "https://apps.shopify.com/judgeme/reviews");
        ScriptTarget(target);
        apifyClient.RunActorAsync(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApifyRunResponse
            {
                Data = new ApifyActorRun { Id = "run-t", Status = ApifyRunStatus.Running },
            });
        apifyClient.GetActorRunAsync("run-t", Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new ApifyRunResponse
            {
                Data = new ApifyActorRun { Id = "run-t", Status = ApifyRunStatus.Running },
            });

        // Bounds shrunk for test speed: give the run 100 ms before giving up.
        var result = await CreateJob(pollInterval: TimeSpan.FromMilliseconds(20), runTimeout: TimeSpan.FromMilliseconds(100))
            .ExecuteAsync(new ScrapeTargetCommand(target.Id));

        Assert.Equal(ScrapeReviewsJobStatus.Failed, result.Status);
        Assert.StartsWith("RunTimedOut", result.FailureReason);
        Assert.Contains("00:00:00.1000000", result.FailureReason);
        await apifyClient.DidNotReceiveWithAnyArgs().GetDatasetItemsAsync(
            default!, default, default, default, default, default);
    }

    [Fact]
    public async Task Fails_when_the_dataset_download_fails()
    {
        var target = Target(
            MarketplaceKind.ShopifyAppStore,
            "https://apps.shopify.com/judgeme/reviews");
        ScriptTarget(target);
        apifyClient.RunActorAsync(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApifyRunResponse
            {
                Data = new ApifyActorRun { Id = "run-d", Status = ApifyRunStatus.Running },
            });
        apifyClient.GetActorRunAsync("run-d", Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new ApifyRunResponse
            {
                Data = new ApifyActorRun
                {
                    Id = "run-d",
                    Status = ApifyRunStatus.Succeeded,
                    DefaultDatasetId = "ds-d",
                },
            });
        apifyClient.GetDatasetItemsAsync(
                "ds-d", Arg.Any<bool?>(), Arg.Any<long?>(), Arg.Any<long?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("storage unavailable"));

        var result = await CreateJob().ExecuteAsync(new ScrapeTargetCommand(target.Id));

        Assert.Equal(ScrapeReviewsJobStatus.Failed, result.Status);
        Assert.StartsWith("DatasetDownloadFailed", result.FailureReason);
        Assert.Contains("HttpRequestException", result.FailureReason);
    }

    [Fact]
    public async Task Fails_when_the_apple_url_carries_no_app_id()
    {
        var target = Target(
            MarketplaceKind.AppleAppStore,
            "https://apps.apple.com/us/app/instagram/");
        ScriptTarget(target);

        var result = await CreateJob().ExecuteAsync(new ScrapeTargetCommand(target.Id));

        Assert.Equal(ScrapeReviewsJobStatus.Failed, result.Status);
        Assert.StartsWith("InvalidTargetUrl", result.FailureReason);
        await apifyClient.DidNotReceiveWithAnyArgs().RunActorAsync(
            default!, default!, default, default, default, default, default);
    }

    [Fact]
    public async Task RunPending_executes_a_dequeued_command_and_nothing_on_an_empty_queue()
    {
        var target = Target(
            MarketplaceKind.ShopifyAppStore,
            "https://apps.shopify.com/judgeme/reviews");
        ScriptTarget(target);
        ScriptSuccessfulRun("run-p", "ds-p", "[]");
        reviewRepository
            .BulkInsertIgnoreDuplicatesAsync(Arg.Any<IReadOnlyList<Review>>(), Arg.Any<CancellationToken>())
            .Returns(0);
        scrapeQueue.DequeueAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => new ScrapeTargetCommand(target.Id),
                _ => (ScrapeTargetCommand?)null);

        var job = CreateJob();
        await job.RunPendingAsync();
        await job.RunPendingAsync();

        await targetRepository.Received(1).MarkScrapedAsync(
            target.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_propagates_cancellation_instead_of_failing()
    {
        var target = Target(
            MarketplaceKind.ShopifyAppStore,
            "https://apps.shopify.com/judgeme/reviews");
        ScriptTarget(target);
        apifyClient.RunActorAsync(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApifyRunResponse
            {
                Data = new ApifyActorRun { Id = "run-c", Status = ApifyRunStatus.Running },
            });
        apifyClient.GetActorRunAsync("run-c", Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new ApifyRunResponse
            {
                Data = new ApifyActorRun { Id = "run-c", Status = ApifyRunStatus.Running },
            });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Host shutdown mid-poll: cancellation propagates (the scheduler
        // records the aborted run), it is not a pipeline failure result.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateJob(pollInterval: TimeSpan.FromMilliseconds(10)).ExecuteAsync(
                new ScrapeTargetCommand(target.Id), cancellation.Token));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Folds_a_client_timeout_into_a_structured_failure_instead_of_treating_it_as_cancellation()
    {
        var target = Target(
            MarketplaceKind.ShopifyAppStore,
            "https://apps.shopify.com/judgeme/reviews");
        ScriptTarget(target);
        // The HttpClient.Timeout shape: a TaskCanceledException while the
        // lifecycle's own token is NOT canceled. That is a step failure —
        // it must reach the structured failure log, not escape as
        // "cancellation" (the review-round regression this pins).
        apifyClient.RunActorAsync(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException(
                "A task was canceled."));

        var result = await CreateJob().ExecuteAsync(new ScrapeTargetCommand(target.Id));

        Assert.Equal(ScrapeReviewsJobStatus.Failed, result.Status);
        Assert.StartsWith("RunStartFailed", result.FailureReason);
        Assert.Contains("TaskCanceledException", result.FailureReason);

        var failure = logger.Entries.Single(entry => entry.Level == LogLevel.Error);
        Assert.Equal(result.JobId.ToString(), failure.State["JobId"]!.ToString());
        Assert.Equal(target.Id.ToString(), failure.State["TargetId"]!.ToString());
        Assert.Equal(result.FailureReason, failure.State["FailureReason"]!.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_non_positive_poll_interval(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateJob(pollInterval: TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Rejects_a_poll_interval_above_the_wait_for_finish_cap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateJob(pollInterval: TimeSpan.FromSeconds(61)));
    }

    [Fact]
    public void Rejects_a_non_positive_run_timeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateJob(runTimeout: TimeSpan.Zero));
    }
}
