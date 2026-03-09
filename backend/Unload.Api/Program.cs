using Unload.Api;
using Unload.Application;
using Unload.Core;
using Unload.Runner;

var builder = WebApplication.CreateBuilder(args);

var root = ApiWorkspacePathResolver.ResolveWorkspaceRoot();
var scriptsDirectory = Path.Combine(root, "scripts");
var catalogPath = Path.Combine(root, "configs", "catalog.json");
var outputDirectory = Path.Combine(root, "output");
var databaseSettings = builder.Configuration
    .GetSection(DatabaseRuntimeSettings.SectionName)
    .Get<DatabaseRuntimeSettings>()
    ?? throw new InvalidOperationException(
        $"Configuration section '{DatabaseRuntimeSettings.SectionName}' is required.");
var runnerOptions = builder.Configuration.GetSection("Runner").Get<RunnerOptions>()
    ?? new RunnerOptions(ChunkSizeBytes: 10 * 1024 * 1024, WorkerCount: 4);

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddUnloadRuntime(new UnloadRuntimePaths(
    CatalogPath: catalogPath,
    ScriptsDirectory: scriptsDirectory,
    OutputDirectory: outputDirectory), databaseSettings, runnerOptions);
builder.Services.AddHostedService<RunProcessingBackgroundService>();

var app = builder.Build();

app.MapControllers();
app.MapHub<RunStatusHub>("/hubs/status");
app.Run();
