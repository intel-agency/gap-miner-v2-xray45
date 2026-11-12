// Placeholder for Story 5.1 (T-4.1): Minimal API endpoints. Epic 2 (domain/data) and
// Story 5.1 build the real endpoint groups (targets, jobs, gaps).
//
// The /connection-strings endpoint exposes which Aspire-injected connection strings are
// visible via IConfiguration (Story 1.2 acceptance criterion) — keys only, never values.
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();

app.MapGet("/", () => "GapMiner.Api placeholder");

app.MapGet("/connection-strings", (IConfiguration configuration) =>
    configuration.GetSection("ConnectionStrings").GetChildren().Select(child => child.Key));

app.MapDefaultEndpoints();

app.Run();
