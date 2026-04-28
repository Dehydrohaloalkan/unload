using Unload.Api.Abstractions;
using Unload.Api.Models;
using Unload.Run.Application;

namespace Unload.Api.Services;

public sealed class HistoryRetentionBackgroundService(
    HistoryRetentionOptions options,
    IRunStateStore runStateStore,
    ITaskExecutionHistoryStore taskExecutionHistoryStore,
    ILogger<HistoryRetentionBackgroundService> logger) : BackgroundService
{
    private readonly HistoryRetentionOptions _options = options;
    private readonly IRunStateStore _runStateStore = runStateStore;
    private readonly ITaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
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

        if (removedRuns > 0 || removedTasks > 0)
        {
            _logger.LogInformation(
                "History retention prune completed. OldestDayToKeepInclusive: {OldestDayToKeepInclusive}, RemovedRuns: {RemovedRuns}, RemovedTaskRecords: {RemovedTasks}",
                oldestDayToKeepInclusive,
                removedRuns,
                removedTasks);
        }

        return Task.CompletedTask;
    }
}

