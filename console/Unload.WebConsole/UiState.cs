namespace Unload.WebConsole;

internal sealed class UiState
{
    private readonly object _sync = new();
    private readonly Queue<RunnerEventLine> _events = new();
    private RunStatusInfoDto? _status;

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

    public void SetStatus(RunStatusInfoDto status)
    {
        lock (_sync)
        {
            _status = status;
        }
    }

    public UiSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new UiSnapshot(
                _status?.Status ?? RunLifecycleStatus.Running,
                _status?.LastStep,
                _status?.Message,
                _status?.UpdatedAt,
                _events.ToArray());
        }
    }
}
