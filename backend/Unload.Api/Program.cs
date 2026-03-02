using Microsoft.AspNetCore.SignalR;
using Unload.Api;
using Unload.Catalog;
using Unload.Core;
using Unload.Cryptography;
using Unload.DataBase;
using Unload.FileWriter;
using Unload.MQ;
using Unload.Runner;

var builder = WebApplication.CreateBuilder(args);

var root = ResolveWorkspaceRoot();
var scriptsDirectory = Path.Combine(root, "scripts");
var catalogPath = Path.Combine(root, "configs", "catalog.json");
var outputDirectory = Path.Combine(root, "output");
var diagnosticsDirectory = ResolveDiagnosticsDirectory(root);

builder.Services.AddSignalR();
builder.Services.AddSingleton<ICatalogService>(_ => new JsonCatalogService(catalogPath, scriptsDirectory));
builder.Services.AddSingleton<IDatabaseClient, StubDatabaseClient>();
builder.Services.AddSingleton<IFileChunkWriter, PipeSeparatedFileChunkWriter>();
builder.Services.AddSingleton<IMqPublisher, InMemoryMqPublisher>();
builder.Services.AddSingleton<IRunDiagnosticsSink>(_ => new CsvRunDiagnosticsSink(diagnosticsDirectory));
builder.Services.AddSingleton<IRequestHasher, Sha256RequestHasher>();
builder.Services.AddSingleton(new RunnerOptions(
    ChunkSizeBytes: 10 * 1024 * 1024,
    MaxDegreeOfParallelism: Math.Max(Environment.ProcessorCount / 2, 1),
    DataflowBoundedCapacity: 8));
builder.Services.AddSingleton<IRunner, RunnerEngine>();
builder.Services.AddSingleton<IRunRequestFactory, RunRequestFactory>();
builder.Services.AddSingleton<IRunQueue, InMemoryRunQueue>();
builder.Services.AddSingleton<IRunStateStore, InMemoryRunStateStore>();
builder.Services.AddSingleton<IRunOrchestrator>(_ => new RunOrchestrator(
    _.GetRequiredService<IRunRequestFactory>(),
    _.GetRequiredService<IRunQueue>(),
    _.GetRequiredService<IRunStateStore>(),
    outputDirectory));
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
    var correlationId = orchestrator.StartRun(request.ProfileCodes);
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

static string ResolveWorkspaceRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (current is not null)
    {
        var catalog = Path.Combine(current.FullName, "configs", "catalog.json");
        var scripts = Path.Combine(current.FullName, "scripts");

        if (File.Exists(catalog) && Directory.Exists(scripts))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException(
        "Workspace root not found. Expected folders: 'configs' with 'catalog.json' and 'scripts'.");
}

static string ResolveDiagnosticsDirectory(string root)
{
    var configured = Environment.GetEnvironmentVariable("UNLOAD_DIAGNOSTICS_DIR");
    return string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(root, "observability")
        : Path.GetFullPath(configured);
}
