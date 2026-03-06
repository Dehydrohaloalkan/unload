using Spectre.Console;
using Unload.Core;

namespace Unload.Console;

/// <summary>
/// Отвечает за интерактивный выбор target-кодов в консоли.
/// </summary>
internal static class TargetCodePrompter
{
    public static async Task<string[]> PromptTargetCodesAsync(
        ICatalogService catalogService,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogService);
        var catalog = await catalogService.GetCatalogAsync(cancellationToken);
        var selectedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var groupedTargets = catalog.Targets
            .GroupBy(static target => new { target.GroupId, target.GroupName })
            .OrderBy(static group => group.Key.GroupName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedTargets)
        {
            var targets = group
                .Select(static target => new
                {
                    target.TargetCode,
                    target.MemberName,
                    target.MemberCode
                })
                .DistinctBy(static target => target.TargetCode, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static target => target.MemberName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (targets.Length == 0)
            {
                continue;
            }

            var labelsToCodes = targets
                .ToDictionary(
                    static target => $"{target.MemberName} (code: {target.MemberCode}, target: {target.TargetCode})",
                    static target => target.TargetCode,
                    StringComparer.OrdinalIgnoreCase);

            var selectedInGroup = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title($"[yellow]{Markup.Escape(group.Key.GroupName)}[/] - выбери таргеты")
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
