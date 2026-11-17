using System.Text.Json;
using GapMiner.Domain.Entities;
using GapMiner.Domain.Enums;
using GapMiner.Infrastructure.Persistence.Repositories;
using GapMiner.Infrastructure.Queueing;
using GapMiner.Infrastructure.Scraping;
using GapMiner.ScraperWorker.Jobs;
using GapMiner.ScraperWorker.Tests.TestSupport;
using NSubstitute;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace GapMiner.ScraperWorker.Tests.Jobs;

/// <summary>
/// One shared Redis container for the ScraperWorker suite — the same
/// redis:7.4 image:tag the AppHost pins; per-test queue keys isolate tests
/// from each other's messages (the Infrastructure suite's queueing pattern).
/// </summary>
public sealed class ScraperRedisFixture : IAsyncLifetime
{
    private readonly RedisContainer container = new RedisBuilder("redis:7.4").Build();

    private IConnectionMultiplexer? multiplexer;

    public IConnectionMultiplexer Redis =>
        multiplexer ?? throw new InvalidOperationException("The fixture is not initialized.");

    async Task IAsyncLifetime.InitializeAsync()
    {
        await container.StartAsync();
        multiplexer = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (multiplexer is not null)
        {
            await multiplexer.DisposeAsync();
        }

        await container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ScraperRedisCollection : ICollectionFixture<ScraperRedisFixture>
{
    public const string Name = "Scraper Redis integration";
}

/// <summary>
/// Story 3.3's queue round-trip integration test: a
/// <see cref="ScrapeTargetCommand"/> enqueued on a real Redis scrape queue is
/// consumed by the job's dequeue entry point (lifecycle step 1), the scrape
/// runs against a mocked Apify (the story's "Apify calls mocked"), and the
/// follow-up <see cref="AnalyzeReviewsCommand"/> appears on the analysis
/// queue (lifecycle step 8) — the Epic 2 queue abstraction doing the real
/// serialization round-trips on both sides.
/// </summary>
[Collection(ScraperRedisCollection.Name)]
public sealed class ScrapeReviewsQueueRoundTripTests
{
    private readonly IApifyClient apifyClient = Substitute.For<IApifyClient>();
    private readonly IReviewRepository reviewRepository = Substitute.For<IReviewRepository>();
    private readonly ICompetitorTargetRepository targetRepository = Substitute.For<ICompetitorTargetRepository>();

    private readonly IJobQueue<ScrapeTargetCommand> scrapeQueue;
    private readonly IJobQueue<AnalyzeReviewsCommand> analysisQueue;

    public ScrapeReviewsQueueRoundTripTests(ScraperRedisFixture fixture)
    {
        var suffix = Guid.NewGuid().ToString("N");
        scrapeQueue = new RedisJobQueue<ScrapeTargetCommand>(
            fixture.Redis, $"gapminer:test:{suffix}:scrape");
        analysisQueue = new RedisJobQueue<AnalyzeReviewsCommand>(
            fixture.Redis, $"gapminer:test:{suffix}:analysis");
    }

    [Fact]
    public async Task An_enqueued_command_round_trips_into_the_analysis_queue()
    {
        var targetId = Guid.NewGuid();
        var target = new CompetitorTarget(
            targetId,
            "Judge.me",
            MarketplaceKind.ShopifyAppStore,
            new Uri("https://apps.shopify.com/judgeme/reviews"),
            new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        targetRepository
            .GetByIdAsync(targetId, Arg.Any<CancellationToken>())
            .Returns(target);

        // Apify mocked: an asynchronous start, an immediate SUCCEEDED poll,
        // and the dataset download returning one mappable 2-star review.
        apifyClient.RunActorAsync(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<int?>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ApifyRunResponse
            {
                Data = new ApifyActorRun { Id = "run-rt", Status = ApifyRunStatus.Running },
            });
        apifyClient.GetActorRunAsync("run-rt", Arg.Any<double?>(), Arg.Any<CancellationToken>())
            .Returns(new ApifyRunResponse
            {
                Data = new ApifyActorRun
                {
                    Id = "run-rt",
                    Status = ApifyRunStatus.Succeeded,
                    DefaultDatasetId = "ds-rt",
                },
            });
        apifyClient.GetDatasetItemsAsync(
                "ds-rt", Arg.Any<bool?>(), Arg.Any<long?>(), Arg.Any<long?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<ApifyDatasetItem>)JsonSerializer.Deserialize<List<ApifyDatasetItem>>(
                """[{"id":"rt-1","review":"Sync errors every night","rating":2,"reviewer":"Store Owner","date":"2026-10-01T00:00:00Z"}]""",
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        reviewRepository
            .BulkInsertIgnoreDuplicatesAsync(Arg.Any<IReadOnlyList<Review>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.ArgAt<IReadOnlyList<Review>>(0).Count);

        var job = new ScrapeReviewsJob(
            apifyClient,
            MarketplaceActorRegistry.Default,
            reviewRepository,
            targetRepository,
            scrapeQueue,
            analysisQueue,
            new SpyLogger<ScrapeReviewsJob>(),
            new DatasetReviewMapper());

        // The round trip: command in on the scrape queue…
        await scrapeQueue.EnqueueAsync(new ScrapeTargetCommand(targetId));

        // …the Hangfire entry point dequeues and executes it…
        await job.RunPendingAsync();

        // …and the follow-up command came out on the analysis queue.
        var analysis = await analysisQueue.DequeueAsync();
        Assert.NotNull(analysis);
        Assert.Equal(targetId, analysis.CompetitorTargetId);

        // The scrape queue is drained, and the scraped review was persisted
        // with the marketplace's own review id as the idempotency key.
        Assert.Null(await scrapeQueue.DequeueAsync());
        var inserted = reviewRepository.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IReviewRepository.BulkInsertIgnoreDuplicatesAsync))
            .GetArguments()[0] as IReadOnlyList<Review>;
        Assert.NotNull(inserted);
        var review = Assert.Single(inserted);
        Assert.Equal("rt-1", review.SourceReviewId);
        Assert.Equal(targetId, review.CompetitorTargetId);
    }

    [Fact]
    public async Task RunPending_on_an_empty_queue_leaves_both_queues_untouched()
    {
        var job = new ScrapeReviewsJob(
            apifyClient,
            MarketplaceActorRegistry.Default,
            reviewRepository,
            targetRepository,
            scrapeQueue,
            analysisQueue,
            new SpyLogger<ScrapeReviewsJob>(),
            new DatasetReviewMapper());

        await job.RunPendingAsync();

        Assert.Null(await scrapeQueue.DequeueAsync());
        Assert.Null(await analysisQueue.DequeueAsync());
        await apifyClient.DidNotReceiveWithAnyArgs().RunActorAsync(
            default!, default!, default, default, default, default, default);
    }
}
