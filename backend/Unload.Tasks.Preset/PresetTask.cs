using Microsoft.Extensions.Logging;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Tasks.Preset;

/// <summary>
/// Задача выполнения preset SQL-скриптов.
/// Синхронная: полностью завершается внутри <see cref="ExecuteAsync"/> и возвращает Completed.
/// Проверку дневного окна и probe делает <see cref="TaskWorkflow"/> через <see cref="DailyWindowPolicy"/>.
/// </summary>
public class PresetTask(
    PresetTaskOptions options,
    PresetScriptExecutor scriptExecutor,
    DailyWindowPolicy dailyWindowPolicy,
    TaskExecutionHistoryStore historyStore,
    ILogger<PresetTask> logger) : UnloadTask
{
    private readonly string _scriptsDirectory = Path.GetFullPath(options.ScriptsDirectory);
    private readonly PresetScriptExecutor _scriptExecutor = scriptExecutor;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly TaskExecutionHistoryStore _historyStore = historyStore;
    private readonly ILogger<PresetTask> _logger = logger;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public override string Code => TaskCodes.Preset;

    public override IReadOnlyCollection<string> RequiresCompleted => [TaskCodes.Probe];

    public override IReadOnlyCollection<string> ConflictsWith => [TaskCodes.Run, TaskCodes.Extra];

    // RequiresDailyWindowOpen = false: особое окно проверяется воркфлоу через window.CanRunPreset.
    public override bool RequiresDailyWindowOpen => false;

    public override async Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Preset task launch rejected: another preset task is already running.");
            throw new InvalidOperationException("Preset task is already running.");
        }

        try
        {
            _logger.LogInformation("Preset task started. ScriptsRoot: {ScriptsDirectory}", _scriptsDirectory);
            var presetDirectory = Path.Combine(_scriptsDirectory, "preset");
            if (!Directory.Exists(presetDirectory))
            {
                throw new DirectoryNotFoundException($"Preset scripts directory was not found: {presetDirectory}");
            }

            var scripts = Directory
                .EnumerateFiles(presetDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var correlationId = BuildCorrelationId("preset");

            if (scripts.Length == 0)
            {
                _logger.LogInformation(
                    "Preset task finished with no scripts. CorrelationId: {CorrelationId}",
                    correlationId);

                var emptyMessage = "No preset scripts found.";
                return RecordAndReturn(correlationId, 0, emptyMessage);
            }

            var tasks = scripts.Select(path => _scriptExecutor.ExecuteAsync(path, correlationId, cancellationToken));
            await Task.WhenAll(tasks);

            _logger.LogInformation(
                "Preset task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}",
                correlationId,
                scripts.Length);

            var message = "Preset scripts executed successfully.";
            return RecordAndReturn(correlationId, scripts.Length, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preset task failed.");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private TaskExecutionResult RecordAndReturn(string correlationId, int scriptsExecuted, string message)
    {
        var now = DateTimeOffset.UtcNow;
        _dailyWindowPolicy.MarkPresetCompleted();
        _historyStore.Add(
            Code,
            now,
            now,
            correlationId,
            message,
            scriptsExecuted,
            filesWritten: 0,
            outputPath: null);

        return new TaskExecutionResult(
            TaskCode: Code,
            ExecutionId: correlationId,
            Status: TaskExecutionStatus.Completed,
            Message: message,
            ScriptsExecuted: scriptsExecuted,
            FilesWritten: 0,
            OutputPath: null);
    }

    private static string BuildCorrelationId(string prefix)
    {
        return $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..43];
    }
}
