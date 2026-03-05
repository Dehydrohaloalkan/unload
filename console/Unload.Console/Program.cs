using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Unload.Application;
using Unload.Core;
using Stopwatch = System.Diagnostics.Stopwatch;

var root = Unload.Console.WorkspacePathResolver.ResolveWorkspaceRoot();
var scriptsDirectory = Path.Combine(root, "scripts");
var catalogPath = Path.Combine(root, "configs", "catalog.json");
var outputDirectory = Path.Combine(root, "output");

var targetCodes = args.Length == 0
    ? await Unload.Console.TargetCodePrompter.PromptTargetCodesAsync(catalogPath, CancellationToken.None)
    : args.SelectMany(static x => x.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

var services = new ServiceCollection();
services.AddUnloadRuntime(new UnloadRuntimePaths(
    CatalogPath: catalogPath,
    ScriptsDirectory: scriptsDirectory,
    OutputDirectory: outputDirectory));

await using var provider = services.BuildServiceProvider().CreateAsyncScope();
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
    await foreach (var @event in runner.RunAsync(request, cts.Token))
    {
        runStateStore.ApplyEvent(@event);
        var color = @event.Step switch
        {
            RunnerStep.Failed => "red",
            RunnerStep.Completed => "green",
            RunnerStep.FileWritten => "deepskyblue1",
            RunnerStep.QueryCompleted => "yellow",
            _ => "grey"
        };

        var line =
            $"[{color}]{@event.OccurredAt:HH:mm:ss}[/] " +
            $"[{color}]{Markup.Escape(@event.Step.ToString())}[/] " +
            $"{Markup.Escape(@event.Message)}";

        if (!string.IsNullOrWhiteSpace(@event.FilePath))
        {
            line += $" [grey]({Markup.Escape(@event.FilePath)})[/]";
        }

        AnsiConsole.MarkupLine(line);
    }
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

static async Task<RunRequest?> WaitForRunRequestAsync(
    IRunCoordinator runCoordinator,
    string correlationId,
    CancellationToken cancellationToken)
{
    await foreach (var request in runCoordinator.ReadActivationsAsync(cancellationToken))
    {
        if (string.Equals(request.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
        {
            return request;
        }
    }

    return null;
}
