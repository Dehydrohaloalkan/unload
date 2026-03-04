using System.Globalization;
using Unload.Core;

namespace Unload.Runner;

/// <summary>
/// Валидации и guard-проверки для запуска выгрузки.
/// </summary>
internal static class RunnerEngineGuard
{
    public const string OutputFilesDirectoryName = "output-files";
    public const string RunReportFileName = "run-report.csv";

    public static void ValidateRequest(RunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            throw new InvalidOperationException("CorrelationId is required.");
        }

        if (request.TargetCodes.Count == 0)
        {
            throw new InvalidOperationException("At least one target code is required.");
        }
    }

    public static void ValidateDatabaseConnectivity(IDatabaseClient databaseClient)
    {
        if (!databaseClient.IsConnected)
        {
            throw new InvalidOperationException("Database connection is not available.");
        }
    }

    public static string CreateRunOutputDirectory(string baseOutputDirectory)
    {
        Directory.CreateDirectory(baseOutputDirectory);

        var timestamp = DateTime.Now.ToString("dd_MM_yyyy_HHmmss", CultureInfo.InvariantCulture);
        var candidate = Path.Combine(baseOutputDirectory, timestamp);
        var suffix = 1;

        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(baseOutputDirectory, $"{timestamp}_{suffix:D2}");
            suffix++;
        }

        Directory.CreateDirectory(candidate);
        return candidate;
    }

    public static string CreateRunFilesDirectory(string runOutputDirectory)
    {
        var path = Path.Combine(runOutputDirectory, OutputFilesDirectoryName);
        Directory.CreateDirectory(path);
        return path;
    }
}
