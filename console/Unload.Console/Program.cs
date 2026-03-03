using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Unload.Console.CatalogSelection;
using Unload.Application;
using Unload.Core;

var root = ResolveWorkspaceRoot();
var scriptsDirectory = Path.Combine(root, "scripts");
var catalogPath = Path.Combine(root, "configs", "catalog.json");
var outputDirectory = Path.Combine(root, "output");
var diagnosticsDirectory = ResolveDiagnosticsDirectory(root);

var profileCodes = args.Length == 0
    ? await PromptProfileCodesAsync(catalogPath, CancellationToken.None)
    : args.SelectMany(static x => x.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

var services = new ServiceCollection();
services.AddUnloadRuntime(new UnloadRuntimePaths(
    CatalogPath: catalogPath,
    ScriptsDirectory: scriptsDirectory,
    OutputDirectory: outputDirectory,
    DiagnosticsDirectory: diagnosticsDirectory));

await using var provider = services.BuildServiceProvider().CreateAsyncScope();
var requestFactory = provider.ServiceProvider.GetRequiredService<IRunRequestFactory>();
var runner = provider.ServiceProvider.GetRequiredService<IRunner>();

AnsiConsole.Write(new Rule("[green]Unload Console[/]").RuleStyle("green").LeftJustified());
AnsiConsole.MarkupLine($"[grey]Catalog:[/] {Markup.Escape(catalogPath)}");
AnsiConsole.MarkupLine($"[grey]Scripts:[/] {Markup.Escape(scriptsDirectory)}");
AnsiConsole.MarkupLine($"[grey]Profiles:[/] {Markup.Escape(string.Join(", ", profileCodes))}");
AnsiConsole.MarkupLine($"[grey]Diagnostics:[/] {Markup.Escape(diagnosticsDirectory)}");
AnsiConsole.MarkupLine(string.Empty);

var request = requestFactory.Create(profileCodes, outputDirectory);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

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

/// <summary>
/// Находит корень workspace по наличию <c>configs/catalog.json</c> и директории <c>scripts</c>.
/// Используется для вычисления runtime-путей консольного приложения.
/// </summary>
/// <returns>Абсолютный путь к корню workspace.</returns>
static string ResolveWorkspaceRoot()
{
    var current = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (current is not null)
    {
        var catalogPath = Path.Combine(current.FullName, "configs", "catalog.json");
        var scriptsPath = Path.Combine(current.FullName, "scripts");

        if (File.Exists(catalogPath) && Directory.Exists(scriptsPath))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException(
        "Workspace root not found. Expected folders: 'configs' with 'catalog.json' and 'scripts'.");
}

/// <summary>
/// Показывает интерактивное меню выбора профилей и возвращает выбранные коды.
/// Используется, когда профили не переданы аргументами командной строки.
/// </summary>
/// <param name="catalogPath">Путь к файлу каталога профилей.</param>
/// <param name="cancellationToken">Токен отмены операции.</param>
/// <returns>Отсортированный список выбранных кодов профилей.</returns>
static async Task<string[]> PromptProfileCodesAsync(string catalogPath, CancellationToken cancellationToken)
{
    var groups = await CatalogSelectionLoader.LoadAsync(catalogPath, cancellationToken);
    var selectedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var group in groups)
    {
        if (group.Profiles.Count == 0)
        {
            continue;
        }

        var labelsToCodes = group.Profiles
            .ToDictionary(
                static x => $"{x.MemberName} [{x.MemberCode}] ({x.ProfileCode})",
                static x => x.ProfileCode,
                StringComparer.OrdinalIgnoreCase);

        var selectedInGroup = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title($"[yellow]{Markup.Escape(group.GroupName)}[/] - выбери профили")
                .NotRequired()
                .InstructionsText("[grey](Space - выбор, Enter - подтвердить)[/]")
                .AddChoices(labelsToCodes.Keys));

        foreach (var label in selectedInGroup)
        {
            selectedProfiles.Add(labelsToCodes[label]);
        }
    }

    if (selectedProfiles.Count == 0)
    {
        throw new InvalidOperationException("Не выбрано ни одного профиля для выгрузки.");
    }

    return selectedProfiles.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
}

/// <summary>
/// Возвращает директорию диагностики из переменной окружения или значение по умолчанию.
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
