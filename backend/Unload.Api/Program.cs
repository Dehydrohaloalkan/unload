using Unload.Api;
using Unload.Api.ErrorHandling;
using Unload.Api.Abstractions;
using Unload.Api.ErrorHandling.Abstractions;
using Unload.Api.Models;
using Unload.Api.Services;
using Unload.Api.UseCases;
using Unload.Api.UseCases.Abstractions;
using Unload.Bootstrapper;
using Unload.Bootstrapper.DependencyInjection;
using Unload.Runner;
using Unload.TaskFlow;
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
builder.Services.AddSingleton<IApiProblemDetailsFactory, ApiProblemDetailsFactory>();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddScoped<IStartRunUseCase, StartRunUseCase>();
builder.Services.AddScoped<IRunPresetUseCase, RunPresetUseCase>();
builder.Services.AddScoped<IRunExtraUseCase, RunExtraUseCase>();
builder.Services.AddScoped<IRequeueToGatewayUseCase, RequeueToGatewayUseCase>();
builder.Services.AddScoped<IGetServerTimeUseCase, GetServerTimeUseCase>();
builder.Services.AddScoped<IOutputFilesService, OutputFilesService>();
builder.Services.AddSingleton<ITaskExecutionHistoryStore, TaskExecutionHistoryStore>();
builder.Services.AddSingleton<IWorkflowInMemoryStateRestorer, WorkflowInMemoryStateRestorer>();
builder.Services.AddSingleton(historyRetentionOptions);
builder.Services.AddSingleton(runtimePaths);
builder.Services.AddUnloadRuntime(runtimePaths, databaseSettings, builder.Configuration, runnerOptions, presetGateOptions);
builder.Services.AddHostedService<WorkflowStateRestoreHostedService>();
builder.Services.AddHostedService<HistoryRetentionBackgroundService>();
builder.Services.AddHostedService<RunProcessingBackgroundService>();
builder.Services.AddHostedService<PresetGateBackgroundService>();
builder.Services.AddHostedService<SenderFeedbackProjectionBackgroundService>();

var app = builder.Build();

app.UseExceptionHandler();
app.MapControllers();
app.MapHub<RunStatusHub>("/hubs/status");
app.Run();
