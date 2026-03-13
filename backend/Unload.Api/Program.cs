using Unload.Api;
using Unload.Api.ErrorHandling;
using Unload.Application;
using Unload.Runner;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Host.UseNLog();

var root = ApiWorkspacePathResolver.ResolveWorkspaceRoot();
var scriptsDirectory = Path.Combine(root, "scripts");
var catalogPath = Path.Combine(root, "configs", "catalog.json");
var outputDirectory = Path.Combine(root, "output");
var runtimePaths = new UnloadRuntimePaths(
    CatalogPath: catalogPath,
    ScriptsDirectory: scriptsDirectory,
    OutputDirectory: outputDirectory);
var databaseSettings = builder.Configuration
    .GetSection(DatabaseRuntimeSettings.SectionName)
    .Get<DatabaseRuntimeSettings>()
    ?? throw new InvalidOperationException(
        $"Configuration section '{DatabaseRuntimeSettings.SectionName}' is required.");
var runnerOptions = builder.Configuration.GetSection("Runner").Get<RunnerOptions>()
    ?? new RunnerOptions(ChunkSizeBytes: 10 * 1024 * 1024, WorkerCount: 4);
var presetGateOptions = builder.Configuration.GetSection("PresetGate").Get<PresetGateOptions>()
    ?? PresetGateOptions.Default;

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddSingleton(presetGateOptions);
builder.Services.AddSingleton<PresetGateStateStore>();
builder.Services.AddSingleton(runtimePaths);
builder.Services.AddUnloadRuntime(runtimePaths, databaseSettings, runnerOptions);
builder.Services.AddHostedService<RunProcessingBackgroundService>();
builder.Services.AddHostedService<PresetGateBackgroundService>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();
app.MapHub<RunStatusHub>("/hubs/status");
app.Run();
