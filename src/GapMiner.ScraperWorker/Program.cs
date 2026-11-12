// Placeholder for Epic 3 (T-2.x): the scraping background worker (Apify client,
// marketplace actor registry, ScrapeReviewsJob). Epic 3 adds hosted services to this
// host; the web host stays so the ServiceDefaults endpoints (/health, /alive) remain
// served (Story 1.3).
//
// At startup it logs whether the Aspire-injected connection strings are visible in its
// environment (Story 1.2 acceptance criterion: they must be accessible to downstream
// projects via IConfiguration, whose environment-variables provider reads exactly these).
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();

app.MapDefaultEndpoints();

var postgresPresent = Environment.GetEnvironmentVariable("ConnectionStrings__gapminer") is not null;
var redisPresent = Environment.GetEnvironmentVariable("ConnectionStrings__redis") is not null;
app.Logger.LogInformation(
    "GapMiner.ScraperWorker placeholder — gapminer connection string present: {Postgres}, redis: {Redis}.",
    postgresPresent,
    redisPresent);

app.Run();
