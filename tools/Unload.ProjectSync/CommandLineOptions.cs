namespace Unload.ProjectSync;

public sealed record CommandLineOptions(
    string? Source,
    string? Target,
    string? Config,
    bool Preview,
    bool ApplyAll,
    bool Yes,
    bool GitMode,
    string? Commit,
    bool ShowHelp)
{
    public static CommandLineOptions Parse(string[] args)
    {
        string? source = null;
        string? target = null;
        string? config = null;
        var preview = false;
        var applyAll = false;
        var yes = false;
        var gitMode = false;
        string? commit = null;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument.ToLowerInvariant())
            {
                case "--source":
                    source = ReadValue(args, ref index, argument);
                    break;
                case "--target":
                    target = ReadValue(args, ref index, argument);
                    break;
                case "--config":
                    config = ReadValue(args, ref index, argument);
                    break;
                case "--preview":
                    preview = true;
                    break;
                case "--apply-all":
                    applyAll = true;
                    break;
                case "--yes":
                    yes = true;
                    break;
                case "--git":
                    gitMode = true;
                    break;
                case "--commit":
                    gitMode = true;
                    commit = ReadValue(args, ref index, argument);
                    break;
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Неизвестный параметр: {argument}");
            }
        }

        if (preview && applyAll)
        {
            throw new ArgumentException("Параметры --preview и --apply-all нельзя использовать вместе.");
        }

        if (yes && !applyAll)
        {
            throw new ArgumentException("Параметр --yes разрешён только вместе с --apply-all.");
        }

        return new CommandLineOptions(
            source,
            target,
            config,
            preview,
            applyAll,
            yes,
            gitMode,
            commit,
            showHelp);
    }

    private static string ReadValue(string[] args, ref int index, string argument)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"После {argument} требуется значение.");
        }

        index++;
        return args[index];
    }
}
