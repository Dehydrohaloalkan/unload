using Unload.Api;
using Unload.Application;
using Unload.Core;

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

builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddUnloadRuntime(new UnloadRuntimePaths(
    CatalogPath: catalogPath,
    ScriptsDirectory: scriptsDirectory,
    OutputDirectory: outputDirectory), databaseSettings);
builder.Services.AddHostedService<RunProcessingBackgroundService>();

var app = builder.Build();

app.MapControllers();
app.MapHub<RunStatusHub>("/hubs/status");
app.Run();
