// Story 1.2 (T-0.2): Aspire AppHost skeleton — orchestrates the whole distributed app.
//
// Infrastructure:
//   - PostgreSQL 16 with the pgvector extension available: the container image is
//     `pgvector/pgvector:pg16` (the plan's risk register names exactly this image; the
//     EF migration in Story 2.2 depends on it via HasPostgresExtension("vector")),
//     with a persistent data volume and the `gapminer` database.
//   - Redis 7.x.
//
// Services: the four projects are placeholders wired with WithReference so connection
// strings flow through IConfiguration in downstream projects; later epics build them out.

// Story 2.2: the postgres password is pinned to a stable local-dev default. With
// WithDataVolume(), Aspire's default random-per-run password only matches the cluster
// on the very first run — every later run would fail auth (28P01) and the Api's
// dev-only ApplyMigrationsAsync at startup would crash the service. Aspire 8.2's
// AddParameter has no inline default (config-only), so the ParameterResource is
// constructed directly — rotate by changing the literal or adding
// Parameters:postgres-password configuration for a real deployment.
var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.CreateResourceBuilder(
    new ParameterResource("postgres-password", _ => "gapminer-local-dev", secret: true));

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
                      .WithImage("pgvector/pgvector")
                      .WithImageTag("pg16")
                      .WithDataVolume()
                      .AddDatabase("gapminer");

var redis = builder.AddRedis("redis")
                   .WithImageTag("7.4");

var api = builder.AddProject<Projects.GapMiner_Api>("api")
                 .WithReference(postgres)
                 .WithReference(redis);

builder.AddProject<Projects.GapMiner_ScraperWorker>("scraper-worker")
       .WithReference(postgres)
       .WithReference(redis);

builder.AddProject<Projects.GapMiner_AIWorker>("ai-worker")
       .WithReference(postgres)
       .WithReference(redis);

builder.AddProject<Projects.GapMiner_Web>("web")
       .WithReference(api);

builder.Build().Run();
