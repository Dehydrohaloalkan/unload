namespace Unload.Store;

internal static class RunTaskCodeResolver
{
    private const string TaskCodeRun = "run";
    private const string TaskCodePreset = "preset";
    private const string TaskCodeExtra = "extra";

    public static string Resolve(string correlationId)
    {
        var normalized = correlationId?.Trim() ?? string.Empty;
        if (normalized.StartsWith("extra-", StringComparison.OrdinalIgnoreCase))
        {
            return TaskCodeExtra;
        }

        if (normalized.StartsWith("preset-", StringComparison.OrdinalIgnoreCase))
        {
            return TaskCodePreset;
        }

        return TaskCodeRun;
    }
}
