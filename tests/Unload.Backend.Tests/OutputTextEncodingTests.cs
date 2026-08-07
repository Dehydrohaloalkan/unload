using Unload.Core;
using Unload.FileWriter;
using Unload.Tasks.ExtraUnload;
using Unload.Tasks.MainUnload;
using Unload.Tasks.MainUnload.Models;

namespace Unload.Backend.Tests;

public class OutputTextEncodingTests
{
    [Fact]
    public void Windows1251_EncodesCyrillicWithExpectedBytes()
    {
        var bytes = OutputTextEncoding.Windows1251.GetBytes("Привет");

        Assert.Equal(new byte[] { 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2 }, bytes);
        Assert.Equal(bytes.Length + 1, PipeDelimitedFormatter.EstimateLineBytes("Привет"));
    }

    [Fact]
    public async Task PipeWriter_CreatesWindows1251FileWithoutBom()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"unload-encoding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            var script = new ScriptDefinition(
                TargetCode: "TARGET",
                ScriptCode: "Y100_TEST",
                OutputFileStem: "Y10",
                OutputFileExtension: ".txt",
                ScriptType: "TYPE",
                ScriptCodes: "100",
                FirstCodeDigit: 1,
                MemberName: "Участник",
                ScriptPath: "test.sql",
                SqlText: "select 1");
            var row = new DatabaseRow(new Dictionary<string, object?> { ["name"] = "Привет" });
            var chunk = new FileChunk(script, 1, [row], PipeDelimitedFormatter.EstimateLineBytes("Привет"));

            var written = await new PipeSeparatedFileChunkWriter().WriteChunkAsync(
                chunk,
                outputDirectory,
                CancellationToken.None);

            var bytes = await File.ReadAllBytesAsync(written.FilePath);
            var decoded = OutputTextEncoding.Windows1251.GetString(bytes);
            Assert.Contains("Привет", decoded, StringComparison.Ordinal);
            Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExtraWriter_CreatesWindows1251FileWithoutBom()
    {
        var outputDirectory = CreateTemporaryDirectory();

        try
        {
            var options = new ExtraUnloadOptions(outputDirectory, outputDirectory);
            var writer = new ExtraOutputWriter(options, new NoopGatewayPublisher());
            var result = new ExtraScriptExecutionResult(
                "BALANCES",
                new Dictionary<string, List<string>> { ["B01"] = ["Привет|банк"] },
                Records: 1);

            var written = await writer.WriteScriptAsync(
                outputDirectory,
                "extra-test",
                result,
                publishToGateway: false,
                CancellationToken.None);

            AssertWindows1251File(written.Files.Single().FilePath, "Привет|банк");
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunReportWriter_CreatesWindows1251FileWithoutBom()
    {
        var outputDirectory = CreateTemporaryDirectory();
        var reportPath = Path.Combine(outputDirectory, "run-report.csv");

        try
        {
            var rows = new[]
            {
                new RunReportRow("Участник", "данные", 0, "result.txt", 1, true, 25)
            };

            await RunReportCsvWriter.WriteAsync(reportPath, rows, CancellationToken.None);

            AssertWindows1251File(reportPath, "Участник");
            AssertWindows1251File(reportPath, "отправлен");
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"unload-encoding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertWindows1251File(string path, string expectedText)
    {
        var bytes = File.ReadAllBytes(path);
        var decoded = OutputTextEncoding.Windows1251.GetString(bytes);
        Assert.Contains(expectedText, decoded, StringComparison.Ordinal);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    private sealed class NoopGatewayPublisher : IGatewayPublisher
    {
        public Task PublishFileBatchReadyAsync(
            SenderFileBatchReadyEvent @event,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PublishSenderFeedbackAsync(
            SenderFileDispatchFeedback feedback,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
