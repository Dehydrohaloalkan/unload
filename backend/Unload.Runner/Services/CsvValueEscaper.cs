namespace Unload.Runner;

/// <summary>
/// Экранирует CSV-значения и защищает от formula-injection.
/// </summary>
internal static class CsvValueEscaper
{
    public static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var safe = value;
        if (safe.Length > 0 && "=+-@".Contains(safe[0], StringComparison.Ordinal))
        {
            safe = $"'{safe}";
        }

        safe = safe.Replace("\"", "\"\"");
        return $"\"{safe}\"";
    }
}
