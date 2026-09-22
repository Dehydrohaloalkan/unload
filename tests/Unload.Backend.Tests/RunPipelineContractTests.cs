using System.Text.Json;
using System.Threading.Channels;
using Unload.Core;
using Unload.Store;
using Unload.Tasks;
using Unload.Tasks.MainUnload;

namespace Unload.Backend.Tests;

public sealed class RunPipelineContractTests
{
    [Fact]
    public async Task TargetLaunch_PopulatesOrderedCaseInsensitiveMemberQueueWithoutResolver()
    {
        using var scratch = new ScratchDirectory();
        using var store = new RunStateStoreFixture();
        var workflow = new RunActivationChannel();
        var catalog = new TargetCatalog();
        var task = new MainUnloadTask(
            catalog,
            new RunRequestFactory(),
            workflow,
            store.Store,
            new RunApplicationOptions(scratch.Path));

        var result = await task.ExecuteAsync(
            new TaskLaunchRequest(
                TaskCodes.Run,
                Codes: ["TWO_MEMBER_B", "one_member_a", "ONE_MEMBER_A"],
                SelectionMode: RunSelectionMode.TargetCodes),
            CancellationToken.None);

        Assert.Equal(TaskExecutionStatus.Accepted, result.Status);
        var state = store.Store.Get(result.ExecutionId);
        Assert.NotNull(state);
        Assert.Equal(
            ["Member B", "Member A"],
            state!.MemberStatuses!.Values.OrderBy(item => item.QueuePosition).Select(item => item.MemberName));
        Assert.Equal(1, state.MemberStatuses["Member B"].QueuePosition);
        Assert.Equal(2, state.MemberStatuses["Member A"].QueuePosition);
        Assert.Equal(1, catalog.GetCatalogCalls);
        Assert.Equal(0, catalog.ResolveCalls);

        workflow.Complete(result.ExecutionId);
    }

    [Fact]
    public async Task RunnerEventSequencer_IsUniqueAndMonotonicUnderConcurrentWriters()
    {
        var sequencer = new RunnerEventSequencer();
        var values = new long[256];

        await Task.WhenAll(values.Select((_, index) => Task.Run(() => values[index] = sequencer.Next())));

        Assert.Equal(values.Length, values.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, values.Length).Select(static value => (long)value), values.Order());
    }

    [Fact]
    public void QueueAndProjectionOrderSurvivePersistenceReload()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start(members: ["Member A", "member a", "Member B"]);
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.ScriptDiscovered,
            "script discovered",
            "Member B",
            "script-b",
            WorkOrder: 2,
            Sequence: 7));
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.QueryStarted,
            "query started",
            "Member B",
            "script-b",
            WorkerId: 2,
            Sequence: 8));

        var reloaded = fixture.Restart().Get("run-1");

        Assert.NotNull(reloaded);
        Assert.Equal(1, reloaded!.MemberStatuses!["Member A"].QueuePosition);
        Assert.Equal(2, reloaded.MemberStatuses["Member B"].QueuePosition);
        Assert.Equal(2, reloaded.ScriptStatuses!["member:8:MEMBER B|script:8:SCRIPT-B"].WorkOrder);
        Assert.Equal(8, reloaded.ScriptStatuses["member:8:MEMBER B|script:8:SCRIPT-B"].Sequence);
    }

    [Fact]
    public void ScopedFailureStopsOnlyTheIdentifiedCardAndPreservesWorkerAssignment()
    {
        using var fixture = new RunStateStoreFixture(workerCount: 2);
        fixture.Start(members: ["Member A", "Member B"]);
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.ScriptDiscovered,
            "discovered",
            "Member A",
            "script-a",
            WorkOrder: 1,
            Sequence: 1));
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.QueryStarted,
            "started",
            "Member A",
            "script-a",
            WorkerId: 2,
            Sequence: 2));
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.FileWriteStarted,
            "writing",
            "Member A",
            "script-a",
            WorkerId: 2,
            ChunkNumber: 1,
            Sequence: 3));

        var failure = new RunnerFailureInfo(
            "file_write",
            "file",
            "member:8:MEMBER A|script:8:SCRIPT-A|chunk:1",
            "Member A",
            "script-a",
            2,
            1,
            "/tmp/a.txt",
            null,
            "RUNNER_FILE_WRITE_FAILED",
            RunnerFailureMessages.ForStage("file_write", "Member A", "script-a"),
            DateTimeOffset.UtcNow);
        fixture.Store.ApplyEvent(new RunnerEvent(
            failure.OccurredAt,
            "run-1",
            RunnerStep.Failed,
            failure.Message,
            failure.MemberName,
            failure.ScriptCode,
            WorkerId: failure.WorkerId,
            ChunkNumber: failure.ChunkNumber,
            FilePath: failure.FilePath,
            Sequence: 4,
            Failure: failure));

        var state = fixture.Store.Get("run-1");
        Assert.NotNull(state);
        Assert.Equal(failure, state!.Failure);
        Assert.Equal(MemberRunLifecycleStatus.Failed, state.MemberStatuses!["Member A"].Status);
        Assert.NotEqual(MemberRunLifecycleStatus.Failed, state.MemberStatuses["Member B"].Status);
        Assert.Equal("failed", state.WorkerStatuses![2].State);
        Assert.Equal("Member A", state.WorkerStatuses[2].MemberName);
        Assert.Equal("script-a", state.WorkerStatuses[2].ScriptCode);
        Assert.Equal(failure, state.ScriptStatuses!["member:8:MEMBER A|script:8:SCRIPT-A"].Failure);
        Assert.Equal(failure, state.FileStatuses!["member:8:MEMBER A|script:8:SCRIPT-A|chunk:1"].Failure);
    }

    [Fact]
    public void ScriptQueryFailureWithoutChunk_DoesNotFailUnrelatedFileCards()
    {
        using var fixture = new RunStateStoreFixture(workerCount: 2);
        fixture.Start(members: ["Member A", "Member B"]);
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.ScriptDiscovered,
            "discovered A",
            "Member A",
            "script-a"));
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.ScriptDiscovered,
            "discovered B",
            "Member B",
            "script-b"));
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.FileWriteStarted,
            "writing A",
            "Member A",
            "script-a",
            WorkerId: 1,
            ChunkNumber: 1,
            Sequence: 1));
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.FileWriteStarted,
            "writing B",
            "Member B",
            "script-b",
            WorkerId: 2,
            ChunkNumber: 1,
            Sequence: 2));

        var failure = new RunnerFailureInfo(
            "query",
            "script",
            "Member A:script-a",
            "Member A",
            "script-a",
            1,
            null,
            null,
            null,
            "RUNNER_QUERY_FAILED",
            "Query execution failed for script 'script-a'.",
            DateTimeOffset.UtcNow);
        fixture.Store.ApplyEvent(new RunnerEvent(
            failure.OccurredAt,
            "run-1",
            RunnerStep.Failed,
            failure.Message,
            failure.MemberName,
            failure.ScriptCode,
            WorkerId: failure.WorkerId,
            Sequence: 3,
            Failure: failure));

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(MemberRunLifecycleStatus.Failed, state.MemberStatuses!["Member A"].Status);
        Assert.NotEqual(MemberRunLifecycleStatus.Failed, state.MemberStatuses["Member B"].Status);
        Assert.Equal("failed", state.WorkerStatuses![1].State);
        Assert.Equal("Member A", state.WorkerStatuses[1].MemberName);
        Assert.Equal("script-a", state.WorkerStatuses[1].ScriptCode);
        Assert.Equal(ScriptRunStage.Failed, state.ScriptStatuses!["member:8:MEMBER A|script:8:SCRIPT-A"].Stage);
        Assert.Equal(ScriptRunStage.AwaitingWorker, state.ScriptStatuses["member:8:MEMBER B|script:8:SCRIPT-B"].Stage);
        Assert.Equal(FileRunStage.QueuedForWrite, state.FileStatuses!["member:8:MEMBER A|script:8:SCRIPT-A|chunk:1"].Stage);
        Assert.Equal(FileRunStage.QueuedForWrite, state.FileStatuses["member:8:MEMBER B|script:8:SCRIPT-B|chunk:1"].Stage);
        Assert.All(state.FileStatuses.Values, file => Assert.Null(file.Failure));
    }

    [Fact]
    public void GatewayBatchSequence_SurvivesFeedbackAndPersistenceReload()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start();
        var occurredAt = DateTimeOffset.UtcNow;
        fixture.Store.ApplyEvent(new RunnerEvent(
            occurredAt,
            "run-1",
            RunnerStep.GatewayBatchQueued,
            "Gateway batch queued.",
            MemberName: "Member A",
            BatchId: "batch-1",
            BatchFileCount: 1,
            Sequence: 17));
        fixture.Store.ApplySenderFeedback(new SenderFileDispatchFeedback(
            occurredAt.AddSeconds(1),
            "run-1",
            "Member A",
            "batch-1",
            SenderFeedbackKind.BatchStarted,
            Message: "Batch started."));

        var beforeRestart = fixture.Store.Get("run-1");
        Assert.Equal(17, beforeRestart!.SenderBatches!["batch-1"].Sequence);

        var afterRestart = fixture.Restart().Get("run-1");
        Assert.Equal(17, afterRestart!.SenderBatches!["batch-1"].Sequence);
    }

    [Fact]
    public async Task RawFailureEvent_IsSanitizedByEmitterAndStateProjection()
    {
        const string sentinel = "SQL password=sentinel; /srv/internal/ftp/path";
        using var fixture = new RunStateStoreFixture(workerCount: 1);
        fixture.Start();
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.ScriptDiscovered,
            "discovered",
            "Member A",
            "script-a"));
        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            RunnerStep.FileWriteStarted,
            "writing",
            "Member A",
            "script-a",
            WorkerId: 1,
            ChunkNumber: 1));

        var rawFailure = new RunnerFailureInfo(
            "query",
            "script",
            "Member A:script-a",
            "Member A",
            "script-a",
            1,
            null,
            null,
            null,
            "RUNNER_QUERY_FAILED",
            sentinel,
            DateTimeOffset.UtcNow);
        fixture.Store.ApplyEvent(new RunnerEvent(
            rawFailure.OccurredAt,
            "run-1",
            RunnerStep.Failed,
            sentinel,
            rawFailure.MemberName,
            rawFailure.ScriptCode,
            WorkerId: rawFailure.WorkerId,
            Failure: rawFailure));

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        var serializedState = JsonSerializer.Serialize(state);
        var persistedState = File.ReadAllText(fixture.StateFilePath);
        Assert.DoesNotContain(sentinel, serializedState, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, persistedState, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, state.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, state.MemberStatuses!["Member A"].Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, state.ScriptStatuses!["member:8:MEMBER A|script:8:SCRIPT-A"].Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, state.WorkerStatuses![1].Failure?.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, state.FileStatuses!["member:8:MEMBER A|script:8:SCRIPT-A|chunk:1"].Message, StringComparison.Ordinal);

        var downstream = Channel.CreateUnbounded<RunnerEvent>();
        var emitter = new RunnerEventEmitter(
            downstream.Writer,
            new RunRequest(["TARGET-1"], "run-2", fixture.ScratchDirectory, false),
            CancellationToken.None);
        await emitter.EmitAsync(
            RunnerStep.Failed,
            sentinel,
            failure: rawFailure);
        await emitter.CompleteAsync();
        var emitted = await downstream.Reader.ReadAsync();

        Assert.DoesNotContain(sentinel, emitted.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, emitted.Failure!.Message, StringComparison.Ordinal);
        Assert.Equal("RUNNER_QUERY_FAILED", emitted.Failure.Code);
    }

    private sealed class TargetCatalog : ICatalogService
    {
        public int GetCatalogCalls { get; private set; }
        public int ResolveCalls { get; private set; }

        public Task<CatalogInfo> GetCatalogAsync(CancellationToken cancellationToken)
        {
            GetCatalogCalls++;
            return Task.FromResult(new CatalogInfo(
                [],
                [],
                [
                    Target("ONE_MEMBER_A", "Member A", 1),
                    Target("TWO_MEMBER_B", "Member B", 2),
                    Target("ONE_MEMBER_A_ALT", "member a", 1)
                ],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
        }

        public Task<(
            IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>> Scripts,
            IReadOnlySet<string> BigScriptTargetCodes)> ResolveAsync(
            IReadOnlyCollection<string> targetCodes,
            CancellationToken cancellationToken)
        {
            ResolveCalls++;
            throw new InvalidOperationException("Target launch must not execute resolver during queue initialization.");
        }

        private static CatalogTargetInfo Target(string targetCode, string memberName, int memberId) => new(
            targetCode,
            1,
            memberId,
            "Group",
            "group",
            "GROUP",
            memberName,
            memberName.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant(),
            ".txt");
    }

    private sealed class ScratchDirectory : IDisposable
    {
        public ScratchDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"unload-contract-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
