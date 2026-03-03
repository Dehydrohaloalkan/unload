namespace Unload.Runner;

/// <summary>
/// Строит безопасный путь директории диагностики запуска.
/// </summary>
internal static class DiagnosticsRunPathHelper
{
    private static readonly char[] InvalidPathChars = Path.GetInvalidFileNameChars();

    public static string GetRunDirectory(string baseDirectory, string correlationId)
    {
        var safeName = correlationId;
        foreach (var invalidChar in InvalidPathChars)
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return Path.Combine(baseDirectory, safeName);
    }
}
