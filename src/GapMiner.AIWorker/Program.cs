// Placeholder for Epic 4 (T-3.x): the LLM-analysis background worker (Semantic Kernel
// bootstrap, EmbedReviewsJob, AnalyzeGapsJob).
//
// At startup it logs whether the Aspire-injected connection strings are visible in its
// environment (Story 1.2 acceptance criterion: they must be accessible to downstream
// projects via IConfiguration, whose environment-variables provider reads exactly these).
var postgresPresent = Environment.GetEnvironmentVariable("ConnectionStrings__gapminer") is not null;
var redisPresent = Environment.GetEnvironmentVariable("ConnectionStrings__redis") is not null;
Console.WriteLine($"GapMiner.AIWorker placeholder — gapminer connection string present: {postgresPresent}, redis: {redisPresent}.");

await Task.Delay(Timeout.Infinite);
