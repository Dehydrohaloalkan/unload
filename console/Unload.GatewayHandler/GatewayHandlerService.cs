using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Unload.GatewayHandler;

public sealed class GatewayHandlerOptions
{
    public string WatchDirectory { get; init; } = "./ftp-root/target";
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);
}

public sealed class GatewayHandlerService(
    GatewayHandlerOptions options,
    ILogger<GatewayHandlerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string absPath = Path.GetFullPath(options.WatchDirectory);
        Directory.CreateDirectory(absPath);

        Console.WriteLine($"[Handler] Watching directory: {absPath}");

        while (!stoppingToken.IsCancellationRequested)
        {
            ProcessFiles(absPath);
            await Task.Delay(options.PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private void ProcessFiles(string directory)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory)
                .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Handler] Failed to enumerate directory: {Dir}", directory);
            return;
        }

        foreach (string filePath in files)
        {
            string filename = Path.GetFileName(filePath);
            try
            {
                long size = new FileInfo(filePath).Length;
                Console.WriteLine($"[Handler] Processing: {filename} ({size} bytes)");
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Handler] Could not process file: {File} — will retry next poll", filename);
            }
        }
    }
}
