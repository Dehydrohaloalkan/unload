namespace Unload.WebConsole;

/// <summary>
/// Потокобезопасное состояние UI для live-обновления панели web-консоли.
/// </summary>
internal sealed class UiState
{
    private readonly object _sync = new();
    private readonly Queue<RunnerEventLine> _events = new();
    private RunStatusInfoDto? _status;
    private PresetGateStateDto? _presetState;

    /// <summary>
    /// Добавляет событие раннера в очередь отображаемых записей.
    /// </summary>
    /// <param name="event">Событие, полученное из SignalR.</param>
    public void AddEvent(RunnerEventDto @event)
    {
        lock (_sync)
        {
            _events.Enqueue(new RunnerEventLine(
                @event.OccurredAt.LocalDateTime,
                @event.Step,
                @event.Message));

            while (_events.Count > 20)
            {
                _events.Dequeue();
            }
        }
    }

    /// <summary>
    /// Обновляет снимок статуса выполнения.
    /// </summary>
    /// <param name="status">Текущий статус запуска.</param>
    public void SetStatus(RunStatusInfoDto status)
    {
        lock (_sync)
        {
            _status = status;
        }
    }

    /// <summary>
    /// Обновляет состояние preset-гейта.
    /// </summary>
    public void SetPresetState(PresetGateStateDto state)
    {
        lock (_sync)
        {
            _presetState = state;
        }
    }

    /// <summary>
    /// Возвращает потокобезопасный снимок данных для отрисовки.
    /// </summary>
    /// <returns>Текущее состояние панели и ленты событий.</returns>
    public UiSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new UiSnapshot(
                _status?.Status ?? RunLifecycleStatus.Running,
                _status?.LastStep,
                _status?.Message,
                _status?.UpdatedAt,
                _events.ToArray(),
                _status?.MemberStatuses?.Values
                    .OrderBy(static x => x.MemberName, StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<MemberRunStatusInfoDto>(),
                _presetState);
        }
    }
}
