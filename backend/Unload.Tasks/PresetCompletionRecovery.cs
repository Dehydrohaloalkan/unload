using Unload.Store;

namespace Unload.Tasks;

/// <summary>
/// Восстанавливает in-memory состояние дневного окна из истории после рестарта приложения.
/// </summary>
public sealed class PresetCompletionRecovery(
    PresetGateOptions options,
    DailyWindowPolicy dailyWindowPolicy,
    TaskExecutionHistoryStore historyStore,
    TimeProvider timeProvider)
{
    private readonly PresetGateOptions _options = options;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly TaskExecutionHistoryStore _historyStore = historyStore;
    private readonly TimeProvider _timeProvider = timeProvider;

    /// <summary>
    /// Возвращает <c>true</c>, только если состояние действительно было восстановлено.
    /// </summary>
    public bool RestoreIfCompletedToday()
    {
        if (!_options.Enabled || _dailyWindowPolicy.Get().PresetCompleted)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        if (!_historyStore.HasRunToday(TaskCodes.Preset, today))
        {
            return false;
        }

        _dailyWindowPolicy.StartPolling();
        _dailyWindowPolicy.MarkPresetCompleted();
        return true;
    }
}
