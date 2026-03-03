using Microsoft.AspNetCore.SignalR;
using Unload.Api;
using Unload.Application;
using Unload.Core;

var builder = WebApplication.CreateBuilder(args);

var root = ResolveWorkspaceRoot();
var scriptsDirectory = Path.Combine(root, "scripts");
var catalogPath = Path.Combine(root, "configs", "catalog.json");
var outputDirectory = Path.Combine(root, "output");
var diagnosticsDirectory = ResolveDiagnosticsDirectory(root);

builder.Services.AddSignalR();
builder.Services.AddUnloadRuntime(new UnloadRuntimePaths(
    CatalogPath: catalogPath,
    ScriptsDirectory: scriptsDirectory,
    OutputDirectory: outputDirectory,
    DiagnosticsDirectory: diagnosticsDirectory));
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

/// <summary>
/// Находит корень workspace по наличию <c>configs/catalog.json</c> и директории <c>scripts</c>.
/// Используется при старте API для вычисления путей runtime.
/// </summary>
/// <returns>Абсолютный путь к корню workspace.</returns>
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

/// <summary>
/// Возвращает директорию диагностики из переменной окружения или значение по умолчанию.
/// Используется при конфигурации runtime-сервисов.
/// </summary>
/// <param name="root">Корневая директория workspace.</param>
/// <returns>Абсолютный путь к директории диагностики.</returns>
static string ResolveDiagnosticsDirectory(string root)
{
    var configured = Environment.GetEnvironmentVariable("UNLOAD_DIAGNOSTICS_DIR");
    return string.IsNullOrWhiteSpace(configured)
        ? Path.Combine(root, "observability")
        : Path.GetFullPath(configured);
}
