// Placeholder for Epic 6 (T-5.x): the Blazor dashboard (layout, targets, jobs,
// opportunity-matrix pages).
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
var app = builder.Build();

app.MapGet("/", () => "GapMiner.Web placeholder");

app.MapDefaultEndpoints();

app.Run();
