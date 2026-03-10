using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Text.RegularExpressions;
using Unload.Application;
using Unload.Core;
using Unload.Runner;
using Stopwatch = System.Diagnostics.Stopwatch;

const int WorkerColumnWidth = 26;
const int MaxGlobalLogs = 15;

var root = Unload.Console.WorkspacePathResolver.ResolveWorkspaceRoot();
var scriptsDirectory = Path.Combine(root, "scripts");
var catalogPath = Path.Combine(root, "configs", "catalog.json");
var outputDirectory = Path.Combine(root, "output");
var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? "Production";
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile($"appsettings.{environment}.json", optional: false, reloadOnChange: false)
    .Build();
var databaseSettings = configuration
    .GetSection(DatabaseRuntimeSettings.SectionName)
    .Get<DatabaseRuntimeSettings>()
    ?? throw new InvalidOperationException(
        $"Configuration section '{DatabaseRuntimeSettings.SectionName}' is required.");
var runnerOptions = configuration.GetSection("Runner").Get<RunnerOptions>()
    ?? new RunnerOptions(ChunkSizeBytes: 10 * 1024 * 1024, WorkerCount: 4);

var services = new ServiceCollection();
services.AddUnloadRuntime(new UnloadRuntimePaths(
    CatalogPath: catalogPath,
    ScriptsDirectory: scriptsDirectory,
    OutputDirectory: outputDirectory), databaseSettings, runnerOptions);

await using var provider = services.BuildServiceProvider().CreateAsyncScope();
var catalogService = provider.ServiceProvider.GetRequiredService<ICatalogService>();
var targetCodes = args.Length == 0
    ? await Unload.Console.TargetCodePrompter.PromptTargetCodesAsync(catalogService, CancellationToken.None)
    : args.SelectMany(static x => x.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
var orchestrator = provider.ServiceProvider.GetRequiredService<IRunOrchestrator>();
var runCoordinator = provider.ServiceProvider.GetRequiredService<IRunCoordinator>();
var runStateStore = provider.ServiceProvider.GetRequiredService<IRunStateStore>();
var runner = provider.ServiceProvider.GetRequiredService<IRunner>();

AnsiConsole.Write(new Rule("[green]Unload Console[/]").RuleStyle("green").LeftJustified());
AnsiConsole.MarkupLine($"[grey]Catalog:[/] {Markup.Escape(catalogPath)}");
AnsiConsole.MarkupLine($"[grey]Scripts:[/] {Markup.Escape(scriptsDirectory)}");
AnsiConsole.MarkupLine($"[grey]Targets:[/] {Markup.Escape(string.Join(", ", targetCodes))}");
AnsiConsole.MarkupLine(string.Empty);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

string correlationId;
try
{
    correlationId = orchestrator.StartRun(targetCodes);
}
catch (RunAlreadyInProgressException ex)
{
    AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
    return;
}

var request = await WaitForRunRequestAsync(runCoordinator, correlationId, cts.Token);
if (request is null)
{
    AnsiConsole.MarkupLine("[red]Run request was not received by processor.[/]");
    return;
}

var runStopwatch = Stopwatch.StartNew();
try
{
    runStateStore.SetRunning(correlationId);
    using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, request.CancellationToken);
    var workerStatuses = Enumerable
        .Range(1, runnerOptions.WorkerCount)
        .ToDictionary(
            static workerId => workerId,
            static _ => "idle");
    var globalLogs = new Queue<string>(MaxGlobalLogs);

    await AnsiConsole.Live(BuildDashboard(workerStatuses, globalLogs))
        .StartAsync(async context =>
    {
        await foreach (var @event in runner.RunAsync(request.Request, runCts.Token))
        {
            runStateStore.ApplyEvent(@event);
            ApplyWorkerStatusUpdate(workerStatuses, @event);
            AppendGlobalLog(globalLogs, FormatEventLine(@event));
            context.UpdateTarget(BuildDashboard(workerStatuses, globalLogs));
            context.Refresh();
        }
    });
}
catch (OperationCanceledException)
{
    runStateStore.SetFailed(correlationId, "Run was cancelled.");
}
catch (Exception ex)
{
    runStateStore.SetFailed(correlationId, ex.Message);
    AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
}
finally
{
    runCoordinator.Complete(correlationId);
}

runStopwatch.Stop();

AnsiConsole.MarkupLine(string.Empty);
AnsiConsole.MarkupLine(
    $"[green]Total export time:[/] [white]{runStopwatch.Elapsed:hh\\:mm\\:ss\\.fff}[/]");

return;

static async Task<RunActivation?> WaitForRunRequestAsync(
    IRunCoordinator runCoordinator,
    string correlationId,
    CancellationToken cancellationToken)
{
    await foreach (var activation in runCoordinator.ReadActivationsAsync(cancellationToken))
    {
        var request = activation.Request;
        if (string.Equals(request.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
        {
            return activation;
        }
    }

    return null;
}

static Rows BuildDashboard(
    IReadOnlyDictionary<int, string> workerStatuses,
    IReadOnlyCollection<string> globalLogs)
{
    return new Rows(
        BuildWorkersTable(workerStatuses),
        BuildGlobalLogsTable(globalLogs));
}

static Table BuildWorkersTable(IReadOnlyDictionary<int, string> workerStatuses)
{
    var table = new Table().Expand();
    foreach (var workerId in workerStatuses.Keys.Order())
    {
        table.AddColumn(
            new TableColumn($"[yellow]Worker #{workerId}[/]")
                .Width(WorkerColumnWidth)
                .NoWrap());
    }

    table.AddRow(workerStatuses.Keys
        .Order()
        .Select(workerId => Markup.Escape(TrimForCell(workerStatuses[workerId], WorkerColumnWidth)))
        .ToArray());

    return table;
}

static Table BuildGlobalLogsTable(IReadOnlyCollection<string> globalLogs)
{
    var logsTable = new Table()
        .Expand()
        .Border(TableBorder.Rounded)
        .Title("Global Logs (last 15)");
    logsTable.AddColumn(new TableColumn("Event").NoWrap(false));

    if (globalLogs.Count == 0)
    {
        logsTable.AddRow("[grey]Waiting for events...[/]");
        return logsTable;
    }

    foreach (var line in globalLogs)
        logsTable.AddRow(Markup.Escape(line));

    return logsTable;
}

static void AppendGlobalLog(Queue<string> globalLogs, string logLine)
{
    globalLogs.Enqueue(logLine);
    while (globalLogs.Count > MaxGlobalLogs)
        globalLogs.Dequeue();
}

static void ApplyWorkerStatusUpdate(IDictionary<int, string> workerStatuses, RunnerEvent @event)
{
    var workerId = TryExtractWorkerId(@event.Message);
    if (workerId is null || !workerStatuses.ContainsKey(workerId.Value))
        return;

    if (@event.Step == RunnerStep.QueryStarted)
    {
        var script = string.IsNullOrWhiteSpace(@event.ScriptCode) ? "unknown script" : @event.ScriptCode;
        workerStatuses[workerId.Value] = $"running {script}";
        return;
    }

    if (@event.Step == RunnerStep.QueryCompleted)
    {
        workerStatuses[workerId.Value] = "idle";
    }
}

static int? TryExtractWorkerId(string message)
{
    var match = Regex.Match(message, @"Worker\s*#(?<id>\d+)", RegexOptions.CultureInvariant);
    if (!match.Success)
        return null;

    return int.TryParse(match.Groups["id"].Value, out var workerId)
        ? workerId
        : null;
}

static string FormatEventLine(RunnerEvent @event)
{
    var line =
        $"{@event.OccurredAt:HH:mm:ss} " +
        $"{@event.Step} " +
        $"{@event.Message}";

    if (!string.IsNullOrWhiteSpace(@event.FilePath))
        line += $" ({@event.FilePath})";

    return line;
}

static string TrimForCell(string value, int width)
{
    if (string.IsNullOrEmpty(value))
        return string.Empty;

    if (value.Length <= width)
        return value;

    if (width <= 1)
        return value[..width];

    if (width <= 3)
        return value[..width];

    return value[..(width - 3)] + "...";
}
