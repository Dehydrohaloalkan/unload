namespace Unload.Api.Abstractions;

/// <summary>
/// Restores in-memory workflow state from persisted history.
/// Intended to run on app startup and on daily window changes.
/// </summary>
public interface IWorkflowInMemoryStateRestorer
{
    void RestoreForToday();
}

