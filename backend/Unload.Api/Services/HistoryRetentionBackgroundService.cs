using Unload.Bootstrapper;
using Unload.Gateway;
using Unload.Store;

namespace Unload.Api.Services;

public sealed class HistoryRetentionBackgroundService(
    HistoryRetentionOptions options,
    RunStateStore runStateStore,
    TaskExecutionHistoryStore taskExecutionHistoryStore,
    GatewayUploadService gatewayUploadService,
    ILogger<HistoryRetentionBackgroundService> logger) : BackgroundService
{
    private readonly HistoryRetentionOptions _options = options;
    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly TaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
    private readonly GatewayUploadService _gatewayUploadService = gatewayUploadService;
    private readonly ILogger<HistoryRetentionBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await PruneOnceAsync(stoppingToken);

        var intervalMinutes = Math.Max(1, _options.PruneIntervalMinutes);
        var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PruneOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            timer.Dispose();
        }
    }

    private Task PruneOnceAsync(CancellationToken cancellationToken)
    {
        if (_options.RetentionDays <= 0)
        {
            return Task.CompletedTask;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var oldestDayToKeepInclusive = today.AddDays(-(Math.Max(1, _options.RetentionDays) - 1));

        var removedRuns = _runStateStore.PruneTerminalRuns(oldestDayToKeepInclusive);
        var removedTasks = _taskExecutionHistoryStore.Prune(oldestDayToKeepInclusive);
        var removedUploads = _gatewayUploadService.PruneStagingDirectories(oldestDayToKeepInclusive);

        if (removedRuns > 0 || removedTasks > 0 || removedUploads > 0)
        {
            _logger.LogInformation(
                "History retention prune completed. OldestDayToKeepInclusive: {OldestDayToKeepInclusive}, RemovedRuns: {RemovedRuns}, RemovedTaskRecords: {RemovedTasks}, RemovedUploadDirs: {RemovedUploads}",
                oldestDayToKeepInclusive,
                removedRuns,
                removedTasks,
                removedUploads);
        }

        return Task.CompletedTask;
    }
}

