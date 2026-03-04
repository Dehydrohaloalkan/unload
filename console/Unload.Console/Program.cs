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
var requestFactory = provider.ServiceProvider.GetRequiredService<IRunRequestFactory>();
var runner = provider.ServiceProvider.GetRequiredService<IRunner>();

AnsiConsole.Write(new Rule("[green]Unload Console[/]").RuleStyle("green").LeftJustified());
AnsiConsole.MarkupLine($"[grey]Catalog:[/] {Markup.Escape(catalogPath)}");
AnsiConsole.MarkupLine($"[grey]Scripts:[/] {Markup.Escape(scriptsDirectory)}");
AnsiConsole.MarkupLine($"[grey]Targets:[/] {Markup.Escape(string.Join(", ", targetCodes))}");
AnsiConsole.MarkupLine(string.Empty);

var request = requestFactory.Create(targetCodes, outputDirectory);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var runStopwatch = Stopwatch.StartNew();
await foreach (var @event in runner.RunAsync(request, cts.Token))
{
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
runStopwatch.Stop();

AnsiConsole.MarkupLine(string.Empty);
AnsiConsole.MarkupLine(
    $"[green]Total export time:[/] [white]{runStopwatch.Elapsed:hh\\:mm\\:ss\\.fff}[/]");

