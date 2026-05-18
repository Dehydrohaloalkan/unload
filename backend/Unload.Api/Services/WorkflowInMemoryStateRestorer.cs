using Unload.Api.Abstractions;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Api.Services;

/// <summary>
/// Восстанавливает in-memory состояние на основе истории выполнения задач.
/// Используется при старте приложения и при смене дневного окна.
/// Сохраняется до Фазы 6; логика упрощена: <see cref="TaskWorkflow"/> читает
/// <see cref="TaskExecutionHistoryStore"/> напрямую, отдельная in-memory копия не нужна.
/// </summary>
public sealed class WorkflowInMemoryStateRestorer(
    DailyWindowPolicy dailyWindowPolicy,
    TaskExecutionHistoryStore taskExecutionHistoryStore,
    ILogger<WorkflowInMemoryStateRestorer> logger) : IWorkflowInMemoryStateRestorer
{
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly TaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
    private readonly ILogger<WorkflowInMemoryStateRestorer> _logger = logger;

    public void RestoreForToday()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);

            // Восстанавливаем состояние DailyWindowPolicy из истории.
            if (_taskExecutionHistoryStore.HasRunToday(TaskCodes.Preset, today))
            {
                _dailyWindowPolicy.MarkPresetCompleted();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore in-memory workflow state from history.");
        }
    }
}
