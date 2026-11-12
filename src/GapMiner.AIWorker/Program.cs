// Placeholder for Epic 4 (T-3.x): the LLM-analysis background worker (Semantic Kernel
// bootstrap, EmbedReviewsJob, AnalyzeGapsJob). Epic 4 adds hosted services to this
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
    "GapMiner.AIWorker placeholder — gapminer connection string present: {Postgres}, redis: {Redis}.",
    postgresPresent,
    redisPresent);

app.Run();
