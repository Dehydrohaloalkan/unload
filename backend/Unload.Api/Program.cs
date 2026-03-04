using Microsoft.AspNetCore.SignalR;
using Unload.Api;
using Unload.Application;
using Unload.Core;

var builder = WebApplication.CreateBuilder(args);

var root = ApiWorkspacePathResolver.ResolveWorkspaceRoot();
var scriptsDirectory = Path.Combine(root, "scripts");
var catalogPath = Path.Combine(root, "configs", "catalog.json");
var outputDirectory = Path.Combine(root, "output");

builder.Services.AddSignalR();
builder.Services.AddUnloadRuntime(new UnloadRuntimePaths(
    CatalogPath: catalogPath,
    ScriptsDirectory: scriptsDirectory,
    OutputDirectory: outputDirectory));
builder.Services.AddHostedService<RunProcessingBackgroundService>();

var app = builder.Build();

app.MapGet("/api/catalog", async (ICatalogService catalogService, CancellationToken cancellationToken) =>
{
    var catalog = await catalogService.GetCatalogAsync(cancellationToken);
    return Results.Ok(catalog);
});

app.MapPost("/api/runs", async (
    RunStartRequest request,
    IRunOrchestrator orchestrator,
    IRunStateStore runStateStore,
    IHubContext<RunStatusHub> hubContext,
    CancellationToken cancellationToken) =>
{
    var correlationId = orchestrator.StartRun(request.TargetCodes);
    var runState = runStateStore.Get(correlationId);
    if (runState is not null)
    {
        await hubContext.Clients.All.SendAsync("run_status", runState, cancellationToken);
    }

    return Results.Accepted(
        $"/api/runs/{correlationId}",
        new RunAcceptedResponse(correlationId, $"/api/runs/{correlationId}", "/hubs/status", "SubscribeRun", "status", "run_status"));
});

app.MapGet("/api/runs", (IRunStateStore runStateStore) => Results.Ok(runStateStore.List()));

app.MapGet("/api/runs/{correlationId}", (string correlationId, IRunStateStore runStateStore) =>
{
    var run = runStateStore.Get(correlationId);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

app.MapHub<RunStatusHub>("/hubs/status");
app.Run();
