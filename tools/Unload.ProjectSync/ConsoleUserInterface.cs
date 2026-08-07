namespace Unload.ProjectSync;

public sealed class ConsoleUserInterface
{
    public string PromptPath(string title, string? defaultValue = null)
    {
        Console.Write(defaultValue is null ? $"{title}: " : $"{title} [{defaultValue}]: ");
        var entered = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(entered)
            ? defaultValue ?? string.Empty
            : entered.Trim('"');
    }

    public void PrintPlan(SyncPlan plan)
    {
        Console.WriteLine();
        WriteColor("План синхронизации", ConsoleColor.Cyan);
        Console.WriteLine($"Источник : {plan.SourceRoot}");
        Console.WriteLine($"Назначение: {plan.TargetRoot}");
        Console.WriteLine();

        foreach (var item in plan.Items)
        {
            var color = item.Action switch
            {
                SyncAction.Add => ConsoleColor.Green,
                SyncAction.Update => ConsoleColor.Yellow,
                SyncAction.Delete => ConsoleColor.Red,
                SyncAction.Protected => ConsoleColor.Magenta,
                SyncAction.TargetOnly => ConsoleColor.DarkGray,
                _ => ConsoleColor.Gray
            };
            WriteColor($"[{ActionLabel(item.Action),-11}] ", color, newLine: false);
            Console.WriteLine(item.TargetRelativePath);
            if (item.CanApply && !string.Equals(
                    item.SourceRelativePath,
                    item.TargetRelativePath,
                    StringComparison.Ordinal))
            {
                Console.WriteLine($"              <- {item.SourceRelativePath}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            $"Добавить: {plan.Items.Count(static x => x.Action == SyncAction.Add)}, " +
            $"обновить: {plan.Items.Count(static x => x.Action == SyncAction.Update)}, " +
            $"удалить: {plan.Items.Count(static x => x.Action == SyncAction.Delete)}, " +
            $"защищено: {plan.Items.Count(static x => x.Action == SyncAction.Protected)}, " +
            $"только в назначении: {plan.Items.Count(static x => x.Action == SyncAction.TargetOnly)}, " +
            $"совпадает: {plan.SameCount}.");
    }

    public IReadOnlyList<SyncPlanItem> SelectItems(IReadOnlyList<SyncPlanItem> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        Console.WriteLine();
        WriteColor("Файлы, доступные для переноса", ConsoleColor.Cyan);
        for (var index = 0; index < candidates.Count; index++)
        {
            Console.WriteLine(
                $"{index + 1,4}. [{ActionLabel(candidates[index].Action),-6}] {candidates[index].TargetRelativePath}");
        }

        Console.WriteLine();
        Console.WriteLine("Введите номера через запятую, диапазон 3-8 или слово all.");
        Console.Write("Выбор (пустая строка — выход): ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        if (string.Equals(input, "all", StringComparison.OrdinalIgnoreCase))
        {
            return candidates.ToArray();
        }

        var indexes = ParseSelection(input, candidates.Count);
        return indexes.Select(index => candidates[index - 1]).ToArray();
    }

    public bool ConfirmApply(int selectedCount)
    {
        Console.WriteLine();
        WriteColor($"Будет перенесено файлов: {selectedCount}.", ConsoleColor.Yellow);
        Console.Write("Для продолжения введите APPLY: ");
        return string.Equals(Console.ReadLine()?.Trim(), "APPLY", StringComparison.Ordinal);
    }

    public GitCommitInfo? SelectCommit(IReadOnlyList<GitCommitInfo> commits)
    {
        Console.WriteLine();
        WriteColor("Последние Git-коммиты", ConsoleColor.Cyan);
        for (var index = 0; index < commits.Count; index++)
        {
            var commit = commits[index];
            Console.WriteLine($"{index + 1,4}. {commit.ShortHash}  {commit.Date}  {commit.Subject}");
        }

        Console.WriteLine();
        Console.WriteLine("Будут показаны только файлы, изменённые выбранным коммитом.");
        Console.Write("Номер коммита (пустая строка — выход): ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (!int.TryParse(input, out var number) || number < 1 || number > commits.Count)
        {
            throw new FormatException($"Номер должен быть от 1 до {commits.Count}.");
        }

        return commits[number - 1];
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Unload.ProjectSync — безопасная синхронизация двух переименованных копий проекта.");
        Console.WriteLine();
        Console.WriteLine("Интерактивный запуск:");
        Console.WriteLine("  Unload.ProjectSync.exe");
        Console.WriteLine();
        Console.WriteLine("Параметры:");
        Console.WriteLine("  --source <path>     Исходный проект разработки");
        Console.WriteLine("  --target <path>     Целевой production-проект");
        Console.WriteLine("  --config <path>     JSON с renames, ignore и protected");
        Console.WriteLine("  --preview           Только показать различия");
        Console.WriteLine("  --apply-all         Применить все безопасные ADD/UPDATE/DELETE");
        Console.WriteLine("  --yes               Не спрашивать APPLY; только с --apply-all");
        Console.WriteLine("  --git               Выбрать один commit и его изменённые файлы");
        Console.WriteLine("  --commit REF        Взять файлы из одного указанного commit");
        Console.WriteLine("  --help              Показать эту справку");
    }

    public static void WriteError(string message)
    {
        WriteColor($"Ошибка: {message}", ConsoleColor.Red);
    }

    private static IReadOnlyList<int> ParseSelection(string value, int maximum)
    {
        var selected = new SortedSet<int>();
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var rangeParts = part.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (rangeParts.Length == 1 && int.TryParse(rangeParts[0], out var single))
            {
                AddChecked(single, maximum, selected);
                continue;
            }

            if (rangeParts.Length == 2 &&
                int.TryParse(rangeParts[0], out var start) &&
                int.TryParse(rangeParts[1], out var end) &&
                start <= end)
            {
                for (var index = start; index <= end; index++)
                {
                    AddChecked(index, maximum, selected);
                }

                continue;
            }

            throw new FormatException($"Не удалось разобрать элемент выбора: '{part}'.");
        }

        return selected.ToArray();
    }

    private static void AddChecked(int value, int maximum, ISet<int> selected)
    {
        if (value < 1 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Номер должен быть от 1 до {maximum}: {value}");
        }

        selected.Add(value);
    }

    private static string ActionLabel(SyncAction action) => action switch
    {
        SyncAction.Add => "ADD",
        SyncAction.Update => "UPDATE",
        SyncAction.Delete => "DELETE",
        SyncAction.Protected => "PROTECTED",
        SyncAction.TargetOnly => "TARGET-ONLY",
        _ => action.ToString().ToUpperInvariant()
    };

    private static void WriteColor(string text, ConsoleColor color, bool newLine = true)
    {
        var originalColor = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            if (newLine)
            {
                Console.WriteLine(text);
            }
            else
            {
                Console.Write(text);
            }
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }
}
