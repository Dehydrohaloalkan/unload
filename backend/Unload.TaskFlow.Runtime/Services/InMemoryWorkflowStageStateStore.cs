using Unload.TaskFlow;

namespace Unload.TaskFlow.Runtime;

/// <summary>
/// Потокобезопасное in-memory хранилище состояния системных workflow-стадий.
/// </summary>
public  class InMemoryWorkflowStageStateStore : IWorkflowStageStateStore
{
    private readonly object _sync = new();
    private readonly HashSet<string> _completedStageCodes = new(StringComparer.OrdinalIgnoreCase);

    public void MarkCompleted(string stageCode)
    {
        lock (_sync)
        {
            _completedStageCodes.Add(stageCode);
        }
    }

    public bool IsCompleted(string stageCode)
    {
        lock (_sync)
        {
            return _completedStageCodes.Contains(stageCode);
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _completedStageCodes.Clear();
        }
    }
}
