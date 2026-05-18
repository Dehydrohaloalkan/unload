using Unload.Store;

namespace Unload.Tasks;

/// <summary>
/// Задача дополнительной выгрузки (extra).
/// Синхронная: выполняется полностью внутри <see cref="ExecuteAsync"/> и возвращает Completed.
/// Проверку дневного окна делает <see cref="TaskWorkflow"/> через <see cref="DailyWindowPolicy"/>.
/// </summary>
public class ExtraUnloadTask(
    IScriptTaskOrchestrator scriptTaskOrchestrator,
    TaskExecutionHistoryStore historyStore) : UnloadTask
{
    private readonly IScriptTaskOrchestrator _scriptTaskOrchestrator = scriptTaskOrchestrator;
    private readonly TaskExecutionHistoryStore _historyStore = historyStore;

    public override string Code => TaskCodes.Extra;

    public override IReadOnlyCollection<string> RequiresCompleted => [TaskCodes.Preset];

    public override IReadOnlyCollection<string> ConflictsWith => [TaskCodes.Preset];

    public override bool RequiresDailyWindowOpen => true;

    public override async Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _scriptTaskOrchestrator.RunExtraAsync(request.PublishToGateway, cancellationToken);

        _historyStore.Add(
            Code,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            result.CorrelationId,
            result.Message,
            result.ScriptsExecuted,
            result.FilesWritten,
            result.OutputPath);

        return new TaskExecutionResult(
            TaskCode: Code,
            ExecutionId: result.CorrelationId,
            Status: TaskExecutionStatus.Completed,
            Message: result.Message,
            ScriptsExecuted: result.ScriptsExecuted,
            FilesWritten: result.FilesWritten,
            OutputPath: result.OutputPath);
    }
}
