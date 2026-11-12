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

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
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
