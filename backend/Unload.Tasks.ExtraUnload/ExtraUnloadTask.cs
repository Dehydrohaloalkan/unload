using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Задача дополнительной выгрузки (extra).
/// Синхронная: выполняется полностью внутри <see cref="ExecuteAsync"/> и возвращает Completed.
/// Проверку дневного окна делает <see cref="TaskWorkflow"/> через <see cref="DailyWindowPolicy"/>.
/// </summary>
public class ExtraUnloadTask(
    ExtraUnloadOptions options,
    ExtraScriptExecutor scriptExecutor,
    ExtraOutputWriter outputWriter,
    TaskExecutionHistoryStore historyStore,
    ILogger<ExtraUnloadTask> logger) : UnloadTask
{
    private readonly string _scriptsDirectory = Path.GetFullPath(options.ScriptsDirectory);
    private readonly string _outputDirectory = Path.GetFullPath(options.OutputDirectory);
    private readonly ExtraScriptExecutor _scriptExecutor = scriptExecutor;
    private readonly ExtraOutputWriter _outputWriter = outputWriter;
    private readonly TaskExecutionHistoryStore _historyStore = historyStore;
    private readonly ILogger<ExtraUnloadTask> _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public override string Code => TaskCodes.Extra;

    public override IReadOnlyCollection<string> RequiresCompleted => [TaskCodes.Preset];

    public override IReadOnlyCollection<string> ConflictsWith => [TaskCodes.Preset];

    public override bool RequiresDailyWindowOpen => true;

    public override async Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Extra task launch rejected: another extra task is already running.");
            throw new InvalidOperationException("Extra scripts task is already running.");
        }

        try
        {
            _logger.LogInformation("Extra task started. ScriptsRoot: {ScriptsDirectory}", _scriptsDirectory);
            if (!Directory.Exists(_scriptsDirectory))
            {
                throw new DirectoryNotFoundException($"Scripts directory was not found: {_scriptsDirectory}");
            }

            var scripts = Directory
                .EnumerateFiles(_scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var correlationId = BuildCorrelationId("extra");

            if (scripts.Length == 0)
            {
                _logger.LogInformation("Extra task finished with no scripts. CorrelationId: {CorrelationId}", correlationId);
                var emptyResult = new TaskExecutionResult(
                    TaskCode: Code,
                    ExecutionId: correlationId,
                    Status: TaskExecutionStatus.Completed,
                    Message: "No root scripts found.",
                    ScriptsExecuted: 0,
                    FilesWritten: 0,
                    OutputPath: null);

                RecordHistory(correlationId, emptyResult);
                return emptyResult;
            }

            var aggregatedLines = new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.OrdinalIgnoreCase);
            var executionTasks = scripts.Select(path =>
                _scriptExecutor.ExecuteAsync(path, correlationId, aggregatedLines, cancellationToken));
            await Task.WhenAll(executionTasks);

            var writeResult = await _outputWriter.WriteAsync(
                _outputDirectory,
                correlationId,
                aggregatedLines,
                request.PublishToGateway,
                cancellationToken);

            _logger.LogInformation(
                "Extra task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}, FilesWritten: {FilesWritten}, OutputPath: {OutputPath}",
                correlationId,
                scripts.Length,
                writeResult.FilesWritten,
                writeResult.OutputPath);

            var result = new TaskExecutionResult(
                TaskCode: Code,
                ExecutionId: correlationId,
                Status: TaskExecutionStatus.Completed,
                Message: "Extra scripts executed and files created.",
                ScriptsExecuted: scripts.Length,
                FilesWritten: writeResult.FilesWritten,
                OutputPath: writeResult.OutputPath);

            RecordHistory(correlationId, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extra task failed.");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void RecordHistory(string correlationId, TaskExecutionResult result)
    {
        _historyStore.Add(
            Code,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            correlationId,
            result.Message,
            result.ScriptsExecuted,
            result.FilesWritten,
            result.OutputPath);
    }

    private static string BuildCorrelationId(string prefix)
    {
        return $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..43];
    }
}
