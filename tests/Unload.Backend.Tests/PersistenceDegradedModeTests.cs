using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class PersistenceDegradedModeTests
{
    [Fact]
    public void RunStateStore_KeepsFirstMutationAndBlocksFollowingMutations()
    {
        using var path = new BlockedPersistencePath();
        var store = new RunStateStore(workerCount: 1, path.StateFilePath);

        var writeFailure = Assert.Throws<IOException>(() => store.SetStarted(
            "run-1",
            targetCodes: ["TARGET-1"],
            memberOrScriptNames: ["Member A"]));
        var retainedState = Assert.IsType<RunStatusInfo>(store.Get("run-1"));

        var unavailable = Assert.Throws<PersistenceUnavailableException>(() => store.ApplyEvent(
            new RunnerEvent(
                DateTimeOffset.UtcNow,
                "run-1",
                RunnerStep.QueryStarted,
                "query started",
                MemberName: "Member A",
                ScriptCode: "script-1",
                WorkerId: 1)));

        Assert.Equal(path.StateFilePath, unavailable.FilePath);
        Assert.Same(writeFailure, unavailable.InnerException);
        Assert.Same(retainedState, store.Get("run-1"));
        Assert.Equal(MemberRunLifecycleStatus.Pending, retainedState.MemberStatuses!["Member A"].Status);
    }

    [Fact]
    public void TaskHistoryStore_KeepsFirstRecordAndBlocksFollowingMutations()
    {
        using var path = new BlockedPersistencePath();
        var store = new TaskExecutionHistoryStore(path.StateFilePath);
        var completedAt = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

        var writeFailure = Assert.Throws<IOException>(() => store.Add(
            "run",
            completedAt.AddMinutes(-1),
            completedAt,
            "run-1",
            "completed"));

        var unavailable = Assert.Throws<PersistenceUnavailableException>(() => store.Add(
            "extra",
            completedAt,
            completedAt.AddMinutes(1),
            "extra-1",
            "completed"));

        Assert.Equal(path.StateFilePath, unavailable.FilePath);
        Assert.Same(writeFailure, unavailable.InnerException);
        Assert.Equal("run-1", Assert.Single(store.List(DateOnly.FromDateTime(completedAt.Date))).CorrelationId);
    }

    private sealed class BlockedPersistencePath : IDisposable
    {
        public BlockedPersistencePath()
        {
            ScratchDirectory = Path.Combine(Path.GetTempPath(), $"unload-degraded-{Guid.NewGuid():N}");
            Directory.CreateDirectory(ScratchDirectory);
            var blockedDirectory = Path.Combine(ScratchDirectory, "blocked");
            File.WriteAllText(blockedDirectory, "this path is a file");
            StateFilePath = Path.Combine(blockedDirectory, "state.json");
        }

        public string ScratchDirectory { get; }

        public string StateFilePath { get; }

        public void Dispose()
        {
            Directory.Delete(ScratchDirectory, recursive: true);
        }
    }
}
