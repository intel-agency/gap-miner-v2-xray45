using System.Diagnostics;
using GapMiner.Infrastructure.Queueing;
using Xunit;

namespace GapMiner.Infrastructure.Tests.Queueing;

/// <summary>
/// Story 2.4 acceptance for <see cref="RedisJobQueue{TCommand}"/>: the
/// Testcontainers.Redis round-trip required by the story (enqueue → dequeue →
/// payload equality), FIFO ordering, queue length, the camelCase / ignore-nulls
/// JSON wire format asserted against the raw stored list entry, blocking
/// dequeue (re-poll within the timeout, clean null on timeout), generic type
/// independence, CancellationToken propagation and constructor validation.
/// </summary>
[Collection(RedisCollection.Name)]
public sealed class RedisJobQueueTests(RedisFixture fixture)
{
    private const int PollIntervalMs = 100;

    /// <summary>Command fixture mirroring the Epic 3 scrape command shape.</summary>
    private sealed record ScrapeTargetCommand(Guid TargetId, string MarketplaceUrl, int? MaxReviews = null);

    private static string NewQueueKey() => $"gapminer:test:{Guid.NewGuid():N}";

    [Fact]
    public async Task Enqueue_then_Dequeue_round_trips_the_command_payload()
    {
        var queue = new RedisJobQueue<ScrapeTargetCommand>(fixture.Redis, NewQueueKey());
        var command = new ScrapeTargetCommand(
            Guid.NewGuid(),
            "https://apps.shopify.com/rank-tracker-pro",
            MaxReviews: 250);

        await queue.EnqueueAsync(command);
        var dequeued = await queue.DequeueAsync();

        Assert.NotNull(dequeued);
        Assert.Equal(command, dequeued);
    }

    [Fact]
    public async Task Dequeue_returns_commands_in_FIFO_order()
    {
        var queue = new RedisJobQueue<ScrapeTargetCommand>(fixture.Redis, NewQueueKey());
        var first = new ScrapeTargetCommand(Guid.NewGuid(), "https://first.example.com");
        var second = new ScrapeTargetCommand(Guid.NewGuid(), "https://second.example.com");
        var third = new ScrapeTargetCommand(Guid.NewGuid(), "https://third.example.com");

        await queue.EnqueueAsync(first);
        await queue.EnqueueAsync(second);
        await queue.EnqueueAsync(third);

        Assert.Equal(first, await queue.DequeueAsync());
        Assert.Equal(second, await queue.DequeueAsync());
        Assert.Equal(third, await queue.DequeueAsync());
    }

    [Fact]
    public async Task LengthAsync_reflects_enqueued_and_dequeued_commands()
    {
        var queue = new RedisJobQueue<ScrapeTargetCommand>(fixture.Redis, NewQueueKey());

        Assert.Equal(0, await queue.LengthAsync());

        await queue.EnqueueAsync(new ScrapeTargetCommand(Guid.NewGuid(), "https://one.example.com"));
        await queue.EnqueueAsync(new ScrapeTargetCommand(Guid.NewGuid(), "https://two.example.com"));
        Assert.Equal(2, await queue.LengthAsync());

        await queue.DequeueAsync();
        Assert.Equal(1, await queue.LengthAsync());
    }

    [Fact]
    public async Task Enqueue_stores_camelCase_JSON_with_nulls_ignored()
    {
        var key = NewQueueKey();
        var queue = new RedisJobQueue<ScrapeTargetCommand>(fixture.Redis, key);
        var command = new ScrapeTargetCommand(
            new Guid("01234567-89ab-cdef-0123-456789abcdef"),
            "https://apps.shopify.com/rank-tracker-pro",
            MaxReviews: null);

        await queue.EnqueueAsync(command);

        var stored = await fixture.Redis.GetDatabase().ListGetByIndexAsync(key, -1);
        Assert.Equal(
            """{"targetId":"01234567-89ab-cdef-0123-456789abcdef","marketplaceUrl":"https://apps.shopify.com/rank-tracker-pro"}""",
            stored.ToString());
    }

    [Fact]
    public async Task Dequeue_times_out_cleanly_when_the_queue_is_empty()
    {
        var queue = new RedisJobQueue<ScrapeTargetCommand>(
            fixture.Redis,
            NewQueueKey(),
            dequeueTimeout: TimeSpan.FromMilliseconds(300),
            pollInterval: TimeSpan.FromMilliseconds(PollIntervalMs));

        var stopwatch = Stopwatch.StartNew();
        var dequeued = await queue.DequeueAsync();
        stopwatch.Stop();

        Assert.Null(dequeued);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(300), $"Timed out after only {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Took unexpectedly long: {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Dequeue_blocks_until_a_command_arrives_within_the_timeout()
    {
        var queue = new RedisJobQueue<ScrapeTargetCommand>(
            fixture.Redis,
            NewQueueKey(),
            dequeueTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(PollIntervalMs));
        var command = new ScrapeTargetCommand(Guid.NewGuid(), "https://late.example.com");
        var enqueuedAt = Task.Delay(500).ContinueWith(_ => queue.EnqueueAsync(command)).Unwrap();

        var stopwatch = Stopwatch.StartNew();
        var dequeued = await queue.DequeueAsync();
        stopwatch.Stop();

        await enqueuedAt;

        Assert.NotNull(dequeued);
        Assert.Equal(command, dequeued);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(450), $"Returned before the enqueue: {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Queues_of_different_command_types_are_independent()
    {
        var scrapeQueue = new RedisJobQueue<ScrapeTargetCommand>(fixture.Redis, NewQueueKey());
        var analyzeQueue = new RedisJobQueue<AnalyzeReviewsCommand>(fixture.Redis, NewQueueKey());
        var scrape = new ScrapeTargetCommand(Guid.NewGuid(), "https://scrape.example.com");
        var analyze = new AnalyzeReviewsCommand(Guid.NewGuid(), ClusterCount: 4);

        await scrapeQueue.EnqueueAsync(scrape);
        await analyzeQueue.EnqueueAsync(analyze);

        Assert.Equal(analyze, await analyzeQueue.DequeueAsync());
        Assert.Equal(scrape, await scrapeQueue.DequeueAsync());
    }

    [Fact]
    public async Task Pre_canceled_CancellationToken_makes_every_method_throw()
    {
        var queue = new RedisJobQueue<ScrapeTargetCommand>(fixture.Redis, NewQueueKey());
        var command = new ScrapeTargetCommand(Guid.NewGuid(), "https://cancel.example.com");
        var cancellationToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => queue.EnqueueAsync(command, cancellationToken));
        await Assert.ThrowsAsync<OperationCanceledException>(() => queue.DequeueAsync(cancellationToken));
        await Assert.ThrowsAsync<OperationCanceledException>(() => queue.LengthAsync(cancellationToken));
    }

    [Fact]
    public void Constructor_rejects_invalid_arguments()
    {
        var redis = fixture.Redis;

        Assert.Throws<ArgumentNullException>(() => new RedisJobQueue<ScrapeTargetCommand>(null!, "gapminer:test:pending"));
        Assert.Throws<ArgumentException>(() => new RedisJobQueue<ScrapeTargetCommand>(redis, "  "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RedisJobQueue<ScrapeTargetCommand>(redis, "gapminer:test:pending", dequeueTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RedisJobQueue<ScrapeTargetCommand>(redis, "gapminer:test:pending", pollInterval: TimeSpan.FromSeconds(-1)));
    }

    private sealed record AnalyzeReviewsCommand(Guid TargetId, int ClusterCount);
}
