using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

internal sealed class RunStateStoreFixture : IDisposable
{
    public RunStateStoreFixture(int workerCount = 2)
    {
        WorkerCount = workerCount;
        ScratchDirectory = Path.Combine(Path.GetTempPath(), $"unload-run-state-{Guid.NewGuid():N}");
        Directory.CreateDirectory(ScratchDirectory);
        StateFilePath = Path.Combine(ScratchDirectory, "runs.json");
        Store = CreateStore();
    }

    public int WorkerCount { get; }

    public string ScratchDirectory { get; }

    public string StateFilePath { get; }

    public RunStateStore Store { get; private set; }

    public string ArtifactPath(string fileName = "result.txt")
    {
        return Path.Combine(ScratchDirectory, fileName);
    }

    public void Start(
        string correlationId = "run-1",
        bool publishToGateway = true,
        string taskCode = "run",
        IReadOnlyCollection<string>? members = null)
    {
        Store.SetStarted(
            correlationId,
            targetCodes: ["TARGET-1"],
            memberNames: members ?? ["Member A"],
            publishToGateway,
            taskCode);
    }

    public void ApplyEvent(
        RunnerStep step,
        string correlationId = "run-1",
        string? memberName = null,
        string? scriptCode = null,
        string? filePath = null,
        int? workerId = null,
        string? message = null)
    {
        Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            correlationId,
            step,
            message ?? step.ToString(),
            memberName,
            scriptCode,
            Records: null,
            filePath,
            workerId));
    }

    public void ApplyFeedback(
        SenderFeedbackKind kind,
        string correlationId = "run-1",
        string memberName = "Member A",
        string batchId = "batch-1",
        string? filePath = null,
        string? message = null)
    {
        Store.ApplySenderFeedback(new SenderFileDispatchFeedback(
            DateTimeOffset.UtcNow,
            correlationId,
            memberName,
            batchId,
            kind,
            filePath,
            message));
    }

    public RunStateStore Restart()
    {
        Store = CreateStore();
        return Store;
    }

    public void Dispose()
    {
        Directory.Delete(ScratchDirectory, recursive: true);
    }

    private RunStateStore CreateStore()
    {
        return new RunStateStore(WorkerCount, StateFilePath);
    }
}
