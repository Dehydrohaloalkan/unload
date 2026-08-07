using System.Globalization;
using Unload.Core;
using Unload.Tasks.MainUnload.Models;

namespace Unload.Tasks.MainUnload;

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
        await using var writer = new StreamWriter(stream, OutputTextEncoding.Windows1251);

        await writer.WriteLineAsync("memberName,fileType,operation,outputFileName,rowsCount,mqStatus,executionTimeMs");
        foreach (var row in orderedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var csvLine = string.Join(",",
                EscapeCsv(row.MemberName),
                EscapeCsv(row.FileType),
                EscapeCsv(MapOperation(row.FirstCodeDigit)),
                EscapeCsv(row.OutputFileName),
                row.RowsCount.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(row.MqSent ? "отправлен" : "не отправлен"),
                row.ExecutionTimeMs.ToString(CultureInfo.InvariantCulture));
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
