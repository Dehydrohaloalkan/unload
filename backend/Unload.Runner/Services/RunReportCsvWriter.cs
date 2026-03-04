using System.Globalization;
using System.Text;

namespace Unload.Runner;

internal static class RunReportCsvWriter
{
    public static async Task WriteAsync(
        string reportPath,
        IReadOnlyCollection<RunReportRow> rows,
        CancellationToken cancellationToken)
    {
        var orderedRows = rows
            .OrderBy(static x => x.MemberName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.OutputFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await using var stream = File.Open(reportPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        await writer.WriteLineAsync("memberName,fileType,operation,outputFileName,rowsCount,mqStatus");
        foreach (var row in orderedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var csvLine = string.Join(",",
                EscapeCsv(row.MemberName),
                EscapeCsv(row.FileType),
                EscapeCsv(MapOperation(row.FirstCodeDigit)),
                EscapeCsv(row.OutputFileName),
                row.RowsCount.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(row.MqSent ? "отправлен" : "не отправлен"));
            await writer.WriteLineAsync(csvLine);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static string MapOperation(int firstCodeDigit)
    {
        return firstCodeDigit switch
        {
            0 => "предоставление",
            2 => "замена",
            _ => firstCodeDigit.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string EscapeCsv(string value)
    {
        var sanitized = value;
        if (!string.IsNullOrEmpty(sanitized) && "=+-@".Contains(sanitized[0]))
        {
            sanitized = $"'{sanitized}";
        }

        var escaped = sanitized.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }
}

internal sealed record RunReportRow(
    string MemberName,
    string FileType,
    int FirstCodeDigit,
    string OutputFileName,
    int RowsCount,
    bool MqSent);
