using Unload.Store;

namespace Unload.Tasks;

/// <summary>
/// Задача выполнения preset-скриптов.
/// Синхронная: выполняется полностью внутри <see cref="ExecuteAsync"/> и возвращает Completed.
/// Проверку дневного окна и probe делает <see cref="TaskWorkflow"/> через <see cref="DailyWindowPolicy"/>.
/// </summary>
public class PresetTask(
    IScriptTaskOrchestrator scriptTaskOrchestrator,
    DailyWindowPolicy dailyWindowPolicy,
    TaskExecutionHistoryStore historyStore) : UnloadTask
{
    private readonly IScriptTaskOrchestrator _scriptTaskOrchestrator = scriptTaskOrchestrator;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly TaskExecutionHistoryStore _historyStore = historyStore;

    public override string Code => TaskCodes.Preset;

    public override IReadOnlyCollection<string> RequiresCompleted => [TaskCodes.Probe];

    public override IReadOnlyCollection<string> ConflictsWith => [TaskCodes.Run, TaskCodes.Extra];

    // RequiresDailyWindowOpen = false: особое окно проверяется воркфлоу через window.CanRunPreset.
    public override bool RequiresDailyWindowOpen => false;

    public override async Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _scriptTaskOrchestrator.RunPresetAsync(cancellationToken);

        _dailyWindowPolicy.MarkPresetCompleted();

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
