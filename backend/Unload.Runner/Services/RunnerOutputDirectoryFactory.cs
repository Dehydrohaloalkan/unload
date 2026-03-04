using System.Globalization;

namespace Unload.Runner;

/// <summary>
/// Создание и именование директорий результатов запуска.
/// </summary>
internal static class RunnerOutputDirectoryFactory
{
    public const string OutputFilesDirectoryName = "output-files";
    public const string RunReportFileName = "run-report.csv";

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
