using Spectre.Console;
using Unload.Console.CatalogSelection;

namespace Unload.Console;

/// <summary>
/// Отвечает за интерактивный выбор target-кодов в консоли.
/// </summary>
internal static class TargetCodePrompter
{
    public static async Task<string[]> PromptTargetCodesAsync(string catalogPath, CancellationToken cancellationToken)
    {
        var groups = await CatalogSelectionLoader.LoadAsync(catalogPath, cancellationToken);
        var selectedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            if (group.Targets.Count == 0)
            {
                continue;
            }

            var labelsToCodes = group.Targets
                .ToDictionary(
                    static x => $"{x.MemberName} [{x.MemberCode}] ({x.TargetCode})",
                    static x => x.TargetCode,
                    StringComparer.OrdinalIgnoreCase);

            var selectedInGroup = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title($"[yellow]{Markup.Escape(group.GroupName)}[/] - выбери таргеты")
                    .NotRequired()
                    .InstructionsText("[grey](Space - выбор, Enter - подтвердить)[/]")
                    .AddChoices(labelsToCodes.Keys));

            foreach (var label in selectedInGroup)
            {
                selectedTargets.Add(labelsToCodes[label]);
            }
        }

        if (selectedTargets.Count == 0)
        {
            throw new InvalidOperationException("Не выбрано ни одного таргета для выгрузки.");
        }

        return selectedTargets.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
