using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Text.RegularExpressions;
using Unload.Bootstrapper;
using Unload.Bootstrapper.DependencyInjection;
using Unload.Core;
using Unload.Store;
using Unload.Tasks;
using Unload.Tasks.MainUnload;
using Stopwatch = System.Diagnostics.Stopwatch;

const int WorkerColumnWidth = 26;
const int MaxGlobalLogs = 15;

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? "Production";
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile($"appsettings.{environment}.json", optional: false, reloadOnChange: false)
    .Build();

var services = new ServiceCollection();
services.AddLogging();
services.AddUnloadRuntime(configuration);

await using var provider = services.BuildServiceProvider().CreateAsyncScope();
var unloadConfig = provider.ServiceProvider.GetRequiredService<UnloadConfiguration>();
var catalogPath = unloadConfig.Paths.CatalogPath;
var scriptsDirectory = unloadConfig.Paths.ScriptsDirectory;
var runnerOptions = unloadConfig.Runner;
var presetGateOptions = unloadConfig.PresetGate;

var catalogService = provider.ServiceProvider.GetRequiredService<ICatalogService>();
var taskWorkflow = provider.ServiceProvider.GetRequiredService<TaskWorkflow>();
var runWorkflow = provider.ServiceProvider.GetRequiredService<RunActivationChannel>();
var runStateStore = provider.ServiceProvider.GetRequiredService<RunStateStore>();
var runner = provider.ServiceProvider.GetRequiredService<MainUnloadEngine>();
var dailyWindowPolicy = provider.ServiceProvider.GetRequiredService<DailyWindowPolicy>();
var mode = args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal)
    ? args[0].Trim().ToLowerInvariant()
    : "--default";

if (mode == "--default" && args.Length == 0)
{
    await RunInteractiveSessionAsync(
        catalogPath,
        scriptsDirectory,
        catalogService,
        taskWorkflow,
        runWorkflow,
        runStateStore,
        runner,
        runnerOptions,
        dailyWindowPolicy,
        presetGateOptions,
        CancellationToken.None);
    return;
}

if (mode is "--preset" or "--extra")
{
    AnsiConsole.Write(new Rule("[green]Unload Console[/]").RuleStyle("green").LeftJustified());
    AnsiConsole.MarkupLine($"[grey]Mode:[/] {Markup.Escape(mode)}");
    using var taskCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        taskCts.Cancel();
    };

    TaskExecutionResult taskResult;
    try
    {
        taskResult = mode == "--preset"
            ? await taskWorkflow.LaunchAsync(
                new TaskLaunchRequest(TaskCode: TaskCodes.Preset),
                taskCts.Token)
            : await taskWorkflow.LaunchAsync(
                new TaskLaunchRequest(TaskCode: TaskCodes.Extra),
                taskCts.Token);
    }
    catch (TaskLaunchException ex)
    {
        AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
        return;
    }

    AnsiConsole.MarkupLine($"[green]Task completed:[/] {Markup.Escape(taskResult.TaskCode)}");
    AnsiConsole.MarkupLine($"[grey]CorrelationId:[/] {Markup.Escape(taskResult.ExecutionId)}");
    AnsiConsole.MarkupLine($"[grey]Scripts:[/] {taskResult.ScriptsExecuted}");
    AnsiConsole.MarkupLine($"[grey]Files:[/] {taskResult.FilesWritten}");
    AnsiConsole.MarkupLine($"[grey]Output:[/] {Markup.Escape(taskResult.OutputPath ?? "-")}");
    return;
}

var targetArgs = mode == "--default" && args.Length > 0 && string.Equals(args[0], "--default", StringComparison.OrdinalIgnoreCase)
    ? args[1..]
    : mode == "--default"
        ? args
        : args[1..];
var targetCodes = targetArgs.Length == 0
    ? await Unload.Console.TargetCodePrompter.PromptTargetCodesAsync(catalogService, CancellationToken.None)
    : targetArgs.SelectMany(static x => x.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

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

try
{
    await ExecuteRunAsync(taskWorkflow, runWorkflow, runStateStore, runner, runnerOptions, targetCodes, cts.Token);
}
catch (TaskLaunchException ex)
{
    AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
    return;
}

return;

static async Task RunInteractiveSessionAsync(
    string catalogPath,
    string scriptsDirectory,
    ICatalogService catalogService,
    TaskWorkflow taskWorkflow,
    RunActivationChannel runWorkflow,
    RunStateStore runStateStore,
    MainUnloadEngine runner,
    RunnerOptions runnerOptions,
    DailyWindowPolicy dailyWindowPolicy,
    PresetGateOptions presetGateOptions,
    CancellationToken cancellationToken)
{
    static void Pause()
    {
        AnsiConsole.MarkupLine(string.Empty);
        AnsiConsole.MarkupLine("[grey]Нажмите любую клавишу, чтобы продолжить...[/]");
        Console.ReadKey(intercept: true);
    }

    while (!cancellationToken.IsCancellationRequested)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[green]Unload Console - Stages[/]").RuleStyle("green").LeftJustified());
        AnsiConsole.MarkupLine($"[grey]Catalog:[/] {Markup.Escape(catalogPath)}");
        AnsiConsole.MarkupLine($"[grey]Scripts:[/] {Markup.Escape(scriptsDirectory)}");
        AnsiConsole.MarkupLine("[grey]Одна сессия для всех фаз: probe -> preset -> run -> extra[/]");
        AnsiConsole.MarkupLine(string.Empty);

        dailyWindowPolicy.RefreshDailyWindowState();
        var state = dailyWindowPolicy.Get();
        RenderStageState(state, presetGateOptions);

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Выберите [green]действие[/]")
                .AddChoices(
                    "Проверить готовность preset (probe)",
                    "Запустить preset",
                    "Запустить run",
                    "Запустить extra",
                    "Обновить состояние",
                    "Выход"));

        if (string.Equals(action, "Выход", StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(action, "Обновить состояние", StringComparison.Ordinal))
        {
            continue;
        }

        try
        {
            if (string.Equals(action, "Проверить готовность preset (probe)", StringComparison.Ordinal))
            {
                await taskWorkflow.LaunchAsync(new TaskLaunchRequest(TaskCode: TaskCodes.Probe), cancellationToken);
                AnsiConsole.MarkupLine($"[grey]Последний probe:[/] {dailyWindowPolicy.Get().LastProbeValue}");
                Pause();
            }
            else if (string.Equals(action, "Запустить preset", StringComparison.Ordinal))
            {
                var presetResult = await taskWorkflow.LaunchAsync(
                    new TaskLaunchRequest(TaskCode: TaskCodes.Preset),
                    cancellationToken);
                AnsiConsole.MarkupLine($"[green]Preset completed.[/] CorrelationId: {Markup.Escape(presetResult.ExecutionId)}");
                Pause();
            }
            else if (string.Equals(action, "Запустить run", StringComparison.Ordinal))
            {
                var targetCodes = await Unload.Console.TargetCodePrompter.PromptTargetCodesAsync(catalogService, cancellationToken);
                await ExecuteRunAsync(taskWorkflow, runWorkflow, runStateStore, runner, runnerOptions, targetCodes, cancellationToken);
                Pause();
            }
            else if (string.Equals(action, "Запустить extra", StringComparison.Ordinal))
            {
                var extraResult = await taskWorkflow.LaunchAsync(
                    new TaskLaunchRequest(TaskCode: TaskCodes.Extra),
                    cancellationToken);
                AnsiConsole.MarkupLine(
                    $"[green]Extra completed.[/] Files: {extraResult.FilesWritten}, Output: {Markup.Escape(extraResult.OutputPath ?? "-")}");
                Pause();
            }
        }
        catch (TaskLaunchException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            Pause();
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Operation cancelled.[/]");
            Pause();
            return;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            Pause();
        }

        AnsiConsole.MarkupLine(string.Empty);
    }
}

static async Task ExecuteRunAsync(
    TaskWorkflow taskWorkflow,
    RunActivationChannel runWorkflow,
    RunStateStore runStateStore,
    MainUnloadEngine runner,
    RunnerOptions runnerOptions,
    IReadOnlyCollection<string> targetCodes,
    CancellationToken cancellationToken)
{
    TaskExecutionResult startRunResult = await taskWorkflow.LaunchAsync(
        new TaskLaunchRequest(
            TaskCode: TaskCodes.Run,
            Codes: targetCodes,
            SelectionMode: RunSelectionMode.TargetCodes),
        cancellationToken);

    var correlationId = startRunResult.ExecutionId;
    var request = await WaitForRunRequestAsync(runWorkflow, correlationId, cancellationToken);
    if (request is null)
    {
        throw new InvalidOperationException("Run request was not received by processor.");
    }

    var runStopwatch = Stopwatch.StartNew();
    try
    {
        runStateStore.SetRunning(correlationId);
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, request.CancellationToken);
        var workerStatuses = Enumerable
            .Range(1, runnerOptions.WorkerCount)
            .ToDictionary(
                static workerId => workerId,
                static _ => "idle");
        var globalLogs = new Queue<string>(MaxGlobalLogs);

        await AnsiConsole.Live(BuildDashboard(workerStatuses, globalLogs))
            .StartAsync(async context =>
            {
                await foreach (var @event in runner.RunAsync(request.Payload, runCts.Token))
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
        throw;
    }
    finally
    {
        runWorkflow.Complete(correlationId);
    }

    runStopwatch.Stop();
    AnsiConsole.MarkupLine(
        $"[green]Total export time:[/] [white]{runStopwatch.Elapsed:hh\\:mm\\:ss\\.fff}[/]");
}

static void RenderStageState(PresetGateState state, PresetGateOptions presetGateOptions)
{
    var table = new Table().Border(TableBorder.Rounded).Title("Workflow Stages");
    table.AddColumn("Stage");
    table.AddColumn("State");
    table.AddColumn("Details");

    var probeState = state.ReadyForPreset
        ? "[green]ready[/]"
        : state.PollingStarted
            ? "[yellow]waiting[/]"
            : "[grey]not started[/]";

    var presetState = state.PresetCompleted
        ? "[green]completed[/]"
        : state.ReadyForPreset
            ? "[yellow]available[/]"
            : "[grey]locked[/]";

    var runAndExtraState = !state.Enabled || state.PresetCompleted
        ? "[green]available[/]"
        : "[grey]locked[/]";

    var now = DateTimeOffset.Now;
    var lastProbeText = state.LastProbeAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    var passTime = ResolveNextPresetWindowStart(now, presetGateOptions.StartHour, presetGateOptions.StartMinute);

    table.AddRow("clock", "[deepskyblue1]now[/]", now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
    table.AddRow("last_probe_time", "[deepskyblue1]time[/]", Markup.Escape(lastProbeText));
    table.AddRow(
        "probe_pass_after",
        "[deepskyblue1]window[/]",
        $"After {passTime:yyyy-MM-dd HH:mm} (local) and probe result = 1");
    table.AddRow("probe_preset_ready", probeState, Markup.Escape(state.Message));
    table.AddRow("preset", presetState, state.PresetCompleted ? "Done for current day" : "Needs probe result = 1");
    table.AddRow("run", runAndExtraState, "Available after preset");
    table.AddRow("extra", runAndExtraState, "Available after preset");

    AnsiConsole.Write(table);
}

static DateTimeOffset ResolveNextPresetWindowStart(DateTimeOffset now, int startHour, int startMinute)
{
    var todayStart = new DateTimeOffset(
        now.Year,
        now.Month,
        now.Day,
        Math.Clamp(startHour, 0, 23),
        Math.Clamp(startMinute, 0, 59),
        0,
        now.Offset);
    return now <= todayStart ? todayStart : todayStart.AddDays(1);
}

static async Task<RunActivation?> WaitForRunRequestAsync(
    RunActivationChannel runWorkflow,
    string correlationId,
    CancellationToken cancellationToken)
{
    await foreach (var activation in runWorkflow.ReadActivationsAsync(cancellationToken))
    {
        if (string.Equals(activation.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
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
    logsTable.AddColumn(new TableColumn("Event"));

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
