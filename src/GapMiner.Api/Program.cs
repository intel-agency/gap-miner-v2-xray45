// Placeholder for Story 5.1 (T-4.1): Minimal API endpoints. Epic 2 (domain/data) and
// Story 5.1 build the real endpoint groups (targets, jobs, gaps).
//
// The /connection-strings endpoint exposes which Aspire-injected connection strings are
// visible via IConfiguration (Story 1.2 acceptance criterion) — keys only, never values.
//
// Story 2.2 (T-1.2): registers GapMinerDbContext against the Aspire-injected
// "gapminer" connection string. In development only, ApplyMigrationsAsync runs at
// startup: it creates the physical database when missing (Aspire 8.x AddDatabase does
// not) and applies the InitialSchema migration. Aspire 8.2 has no WaitFor, so the
// AppHost does not gate this service on postgres readiness — the bounded connection
// retry inside ApplyMigrationsAsync bridges container boot instead. Production
// applies migrations via the deployment pipeline instead.
using GapMiner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddDbContext<GapMinerDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("gapminer"),
        npgsql => npgsql.UseVector()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.ApplyMigrationsAsync();
}

app.MapGet("/", () => "GapMiner.Api placeholder");

app.MapGet("/connection-strings", (IConfiguration configuration) =>
    configuration.GetSection("ConnectionStrings").GetChildren().Select(child => child.Key));

app.MapDefaultEndpoints();

app.Run();
