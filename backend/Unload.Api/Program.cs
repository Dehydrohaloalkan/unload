using Unload.Api;
using Unload.Api.ErrorHandling;
using Unload.Api.Models;
using Unload.Api.Services;
using Unload.Bootstrapper;
using Unload.Bootstrapper.DependencyInjection;
using Unload.Tasks;
using Unload.Tasks.MainUnload;
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
var historyRetentionOptions = builder.Configuration
    .GetSection(HistoryRetentionOptions.SectionName)
    .Get<HistoryRetentionOptions>()
    ?? HistoryRetentionOptions.Default;

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<ApiProblemDetailsFactory>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddScoped<OutputFilesService>();
builder.Services.AddSingleton(historyRetentionOptions);
builder.Services.AddSingleton(runtimePaths);
builder.Services.AddUnloadRuntime(runtimePaths, databaseSettings, builder.Configuration, runnerOptions, presetGateOptions);
builder.Services.AddHostedService<HistoryRetentionBackgroundService>();
builder.Services.AddHostedService<MainUnloadHostedService>();
builder.Services.AddHostedService<ProbeSchedulerHostedService>();
builder.Services.AddHostedService<SenderFeedbackProjectionBackgroundService>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();
app.MapHub<RunStatusHub>("/hubs/status");
app.Run();
