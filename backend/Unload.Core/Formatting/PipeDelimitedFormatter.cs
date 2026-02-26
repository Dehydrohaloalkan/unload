using System.Text;

namespace Unload.Core;

public static class PipeDelimitedFormatter
{
    public static IReadOnlyList<string> GetOrderedColumns(IReadOnlyList<DatabaseRow> rows)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            foreach (var key in row.Columns.Keys)
            {
                if (seen.Add(key))
                {
                    ordered.Add(key);
                }
            }
        }

        return ordered;
    }

    public static string BuildHeaderLine(IReadOnlyList<string> columns)
    {
        return string.Join('|', columns.Select(Escape));
    }

    public static string BuildDataLine(DatabaseRow row, IReadOnlyList<string> columns)
    {
        var values = new string[columns.Count];

        for (var i = 0; i < columns.Count; i++)
        {
            var key = columns[i];
            row.Columns.TryGetValue(key, out var rawValue);
            values[i] = Escape(rawValue?.ToString() ?? string.Empty);
        }

        return string.Join('|', values);
    }

    public static int EstimateLineBytes(string line)
    {
        return Encoding.UTF8.GetByteCount(line) + 1;
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
