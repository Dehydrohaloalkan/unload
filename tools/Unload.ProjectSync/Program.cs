namespace Unload.ProjectSync;

public static class Program
{
    public static int Main(string[] args)
    {
        var ui = new ConsoleUserInterface();

        try
        {
            var options = CommandLineOptions.Parse(args);
            if (options.ShowHelp)
            {
                ConsoleUserInterface.PrintHelp();
                return 0;
            }

            Console.WriteLine("Unload.ProjectSync");
            Console.WriteLine("Файлы не изменяются до явного подтверждения.");

            var source = options.Source ?? ui.PromptPath("Папка проекта разработки");
            var target = options.Target ?? ui.PromptPath("Папка production-проекта");

            return options.GitMode
                ? RunGitMode(options, ui, source, target)
                : RunDirectoryMode(options, ui, source, target);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            ConsoleUserInterface.WriteError(exception.Message);
            return 1;
        }
    }

    private static int RunDirectoryMode(
        CommandLineOptions options,
        ConsoleUserInterface ui,
        string source,
        string target)
    {
        var configuration = LoadConfiguration(options, ui);
        var textTransformer = new TextFileTransformer();
        var planner = new SyncPlanner(new GlobMatcher(), textTransformer);
        var plan = planner.CreatePlan(source, target, configuration);
        ui.PrintPlan(plan);

        return ApplyPlan(options, ui, plan, configuration, textTransformer);
    }

    private static int RunGitMode(
        CommandLineOptions options,
        ConsoleUserInterface ui,
        string source,
        string target)
    {
        Console.WriteLine("Git-режим учитывает только закоммиченные изменения.");
        var git = new GitClient();
        var repositoryRoot = Path.TrimEndingDirectorySeparator(git.GetRepositoryRoot(source));
        var targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Trim().Trim('"')));
        if (!Directory.Exists(targetRoot))
        {
            throw new DirectoryNotFoundException($"Production-каталог не найден: {targetRoot}");
        }

        var selectedCommit = options.Commit is not null
            ? git.GetCommit(repositoryRoot, options.Commit)
            : ui.SelectCommit(git.GetRecentCommits(repositoryRoot));
        if (selectedCommit is null)
        {
            Console.WriteLine("Commit не выбран. Изменения не применялись.");
            return 0;
        }

        var configuration = LoadConfiguration(options, ui);
        var textTransformer = new TextFileTransformer();
        var planner = new GitSyncPlanner(git, new GlobMatcher(), textTransformer);
        var plan = planner.CreatePlan(
            repositoryRoot,
            targetRoot,
            selectedCommit.Hash,
            configuration);

        Console.WriteLine();
        Console.WriteLine($"Выбран commit: {selectedCommit.ShortHash} — {selectedCommit.Subject}");
        ui.PrintPlan(plan);

        return ApplyPlan(options, ui, plan, configuration, textTransformer);
    }

    private static int ApplyPlan(
        CommandLineOptions options,
        ConsoleUserInterface ui,
        SyncPlan plan,
        SyncConfiguration configuration,
        TextFileTransformer textTransformer)
    {
        if (options.Preview)
        {
            Console.WriteLine("Режим preview: изменения не применялись.");
            return 0;
        }

        var candidates = plan.ApplicableItems;
        if (candidates.Count == 0)
        {
            Console.WriteLine("Нет файлов для автоматического переноса.");
            return 0;
        }

        var selected = options.ApplyAll
            ? candidates
            : ui.SelectItems(candidates);
        if (selected.Count == 0)
        {
            Console.WriteLine("Ничего не выбрано. Изменения не применялись.");
            return 0;
        }

        var result = new SyncExecutor(textTransformer).Execute(plan, selected, configuration);
        Console.WriteLine();
        Console.WriteLine(
            $"Готово. Добавлено: {result.Added}, обновлено: {result.Updated}, удалено: {result.Deleted}.");
        if (result.BackupDirectory is not null)
        {
            Console.WriteLine($"Резервные копии ({result.BackupCount}): {result.BackupDirectory}");
        }

        return 0;
    }

    private static SyncConfiguration LoadConfiguration(
        CommandLineOptions options,
        ConsoleUserInterface ui)
    {
        var configPath = options.Config ?? FindDefaultConfigPath() ?? ui.PromptPath(
            "Файл конфигурации",
            Path.Combine(Environment.CurrentDirectory, "project-sync.json"));
        return SyncConfiguration.Load(Path.GetFullPath(configPath));
    }

    private static string? FindDefaultConfigPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "project-sync.json"),
            Path.Combine(AppContext.BaseDirectory, "project-sync.json")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
