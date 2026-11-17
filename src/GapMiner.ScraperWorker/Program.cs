// Story 3.3 (T-2.3): the scraping worker's composition root. Hangfire runs
// ScrapeReviewsJob as the recurring scrape-reviews job (every minute) which
// dequeues ScrapeTargetCommands from the Redis scrape queue and executes the
// 9-step scrape lifecycle per command. Everything the job consumes comes from
// the Epic 2/3 layers: the Redis queue abstraction, the EF repositories, the
// Apify Refit client and the marketplace actor registry.
//
// The Redis connection is deliberately ONE multiplexer shared by the command
// queues and Hangfire's job storage (the client's recommended pattern), fed by
// the Aspire-injected "redis" connection string (Story 1.2).
//
// The Hangfire dashboard (/hangfire) is exposed read-only for local operators;
// its default authorization allows local requests only. The web host itself
// stays so the ServiceDefaults health endpoints (/health, /alive) remain
// served (Story 1.3).
using GapMiner.Infrastructure.Persistence;
using GapMiner.Infrastructure.Persistence.Repositories;
using GapMiner.Infrastructure.Queueing;
using GapMiner.Infrastructure.Scraping;
using GapMiner.ScraperWorker.Jobs;
using Hangfire;
using Hangfire.Common;
using Hangfire.Redis.StackExchange;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var redisConnectionString = builder.Configuration.GetConnectionString("redis")
    ?? throw new InvalidOperationException(
        "The redis connection string is not configured; the scrape pipeline's queues and job storage both need it.");
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton<IJobQueue<ScrapeTargetCommand>>(serviceProvider =>
    new RedisJobQueue<ScrapeTargetCommand>(
        serviceProvider.GetRequiredService<IConnectionMultiplexer>(),
        GapMinerQueueKeys.ScrapePending));
builder.Services.AddSingleton<IJobQueue<AnalyzeReviewsCommand>>(serviceProvider =>
    new RedisJobQueue<AnalyzeReviewsCommand>(
        serviceProvider.GetRequiredService<IConnectionMultiplexer>(),
        GapMinerQueueKeys.AnalysisPending));

builder.Services.AddDbContext<GapMinerDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("gapminer"),
        npgsql => npgsql.UseVector()));
builder.Services.AddGapMinerRepositories();

builder.Services.AddApifyClient(builder.Configuration);
builder.Services.AddSingleton(MarketplaceActorRegistry.Default);
builder.Services.AddSingleton<DatasetReviewMapper>();
builder.Services.AddScoped<ScrapeReviewsJob>();

builder.Services.AddHangfire((serviceProvider, config) => config
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseRedisStorage(serviceProvider.GetRequiredService<IConnectionMultiplexer>()));
builder.Services.AddHangfireServer();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseHangfireDashboard();

using (var scope = app.Services.CreateScope())
{
    // Every minute (Hangfire's guaranteed recurring granularity), the tick
    // dequeues at most one command and runs the full scrape lifecycle for it;
    // overlapping ticks are separate jobs, so queue concurrency is bounded by
    // the Hangfire server's worker pool, not the cron.
    //
    // CancellationToken.None is Hangfire's documented placeholder, not a dead
    // wire: CoreBackgroundJobPerformer substitutes every CancellationToken
    // parameter with its own shutdown-aware token just before performing the
    // job, so host shutdown does cancel an in-flight lifecycle.
    var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobs.AddOrUpdate(
        "scrape-reviews",
        Job.FromExpression<ScrapeReviewsJob>(job => job.RunPendingAsync(CancellationToken.None)),
        ScrapeReviewsJob.RecurrenceCron,
        new RecurringJobOptions());
}

app.Logger.LogInformation(
    "GapMiner.ScraperWorker running: scrape queue {ScrapeQueue}, analysis queue {AnalysisQueue}, "
    + "recurring job '{JobId}' on cron {Cron}.",
    GapMinerQueueKeys.ScrapePending,
    GapMinerQueueKeys.AnalysisPending,
    "scrape-reviews",
    ScrapeReviewsJob.RecurrenceCron);

app.Run();
