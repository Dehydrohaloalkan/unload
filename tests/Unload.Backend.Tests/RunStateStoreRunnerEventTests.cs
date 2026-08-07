using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class RunStateStoreRunnerEventTests
{
    [Theory]
    [InlineData(TerminalMutation.Failed)]
    [InlineData(TerminalMutation.CancellationRequested)]
    [InlineData(TerminalMutation.Cancelled)]
    public void TerminalMutation_ForUnknownRunThrows(TerminalMutation mutation)
    {
        using var fixture = new RunStateStoreFixture();

        var action = () =>
        {
            switch (mutation)
            {
                case TerminalMutation.Failed:
                    fixture.Store.SetFailed("missing", "failed");
                    break;
                case TerminalMutation.CancellationRequested:
                    fixture.Store.SetCancellationRequested("missing", "stop requested");
                    break;
                case TerminalMutation.Cancelled:
                    fixture.Store.SetCancelled("missing", "cancelled");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
        };

        var exception = Assert.Throws<KeyNotFoundException>(action);
        Assert.Contains("missing", exception.Message);
    }

    [Fact]
    public void SetStarted_CreatesRunningStateWithPendingMembersAndIdleWorkers()
    {
        using var fixture = new RunStateStoreFixture(workerCount: 3);

        fixture.Start(members: ["Member A", "member a", "Member B"]);

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Running, state.Status);
        Assert.Equal("run", state.TaskCode);
        Assert.True(state.PublishToGateway);
        Assert.Equal(["TARGET-1"], state.TargetCodes);
        Assert.Equal(2, state.MemberStatuses!.Count);
        Assert.All(state.MemberStatuses.Values, member => Assert.Equal(MemberRunLifecycleStatus.Pending, member.Status));
        Assert.Equal(3, state.WorkerStatuses!.Count);
        Assert.All(state.WorkerStatuses.Values, worker => Assert.Equal("idle", worker.State));
    }

    [Fact]
    public void ProgressEvents_ProjectWorkerMemberAndArtifactState()
    {
        using var fixture = new RunStateStoreFixture();
        var artifactPath = fixture.ArtifactPath();
        fixture.Start();

        fixture.ApplyEvent(
            RunnerStep.QueryStarted,
            memberName: "Member A",
            scriptCode: "script-a",
            workerId: 2);
        fixture.ApplyEvent(
            RunnerStep.FileWritten,
            memberName: "Member A",
            scriptCode: "script-a",
            filePath: artifactPath,
            workerId: 2);
        fixture.ApplyEvent(
            RunnerStep.FileWritten,
            memberName: "Member A",
            scriptCode: "script-a",
            filePath: artifactPath,
            workerId: 2);
        fixture.ApplyEvent(
            RunnerStep.QueryCompleted,
            memberName: "Member A",
            scriptCode: "script-a",
            workerId: 2);
        fixture.ApplyEvent(
            RunnerStep.ScriptCompleted,
            memberName: "Member A",
            scriptCode: "script-a",
            workerId: 2);

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        var member = state.MemberStatuses!["Member A"];
        Assert.Equal(MemberRunLifecycleStatus.Completed, member.Status);
        Assert.Equal(RunnerStep.ScriptCompleted, member.LastStep);
        var artifact = Assert.Single(state.OutputArtifacts!);
        Assert.Equal("result.txt", artifact.FileName);
        Assert.Equal(artifactPath, artifact.FilePath);
        Assert.Equal("script-a", artifact.ScriptCode);
        Assert.Equal("idle", state.WorkerStatuses![2].State);
        Assert.Null(state.WorkerStatuses[2].ScriptCode);
    }

    [Fact]
    public void CompletedWithoutGateway_BecomesTerminalAndAddsSkippedBatches()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start(publishToGateway: false, members: ["Member A", "Member B"]);
        fixture.ApplyEvent(
            RunnerStep.FileWritten,
            memberName: "Member A",
            scriptCode: "script-a",
            filePath: fixture.ArtifactPath());

        fixture.ApplyEvent(RunnerStep.Completed, filePath: fixture.ScratchDirectory);

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Completed, state.Status);
        Assert.Equal(RunnerStep.Completed, state.LastStep);
        Assert.Equal(fixture.ScratchDirectory, state.OutputPath);
        Assert.Equal(2, state.SenderBatches!.Count);
        Assert.All(
            state.SenderBatches.Values,
            batch => Assert.Equal(SenderBatchStatus.SkippedByRequest, batch.Status));
        Assert.All(
            state.MemberStatuses!.Values,
            member => Assert.Equal(MemberRunLifecycleStatus.Completed, member.Status));
        Assert.All(state.WorkerStatuses!.Values, worker => Assert.Equal("idle", worker.State));
    }

    [Fact]
    public void FailedRunnerEvent_FailsMembersAndResetsWorkers()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start(members: ["Member A", "Member B"]);
        fixture.ApplyEvent(
            RunnerStep.QueryStarted,
            memberName: "Member A",
            scriptCode: "script-a",
            workerId: 1);

        fixture.ApplyEvent(RunnerStep.Failed, message: "database failed");

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Failed, state.Status);
        Assert.Equal(RunnerStep.Failed, state.LastStep);
        Assert.Equal("database failed", state.Message);
        Assert.All(
            state.MemberStatuses!.Values,
            member => Assert.Equal(MemberRunLifecycleStatus.Failed, member.Status));
        Assert.All(state.WorkerStatuses!.Values, worker => Assert.Equal("idle", worker.State));
    }

    [Fact]
    public void ExplicitFailure_FailsAllMembersAndResetsWorkers()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start();
        fixture.ApplyEvent(
            RunnerStep.QueryStarted,
            memberName: "Member A",
            scriptCode: "script-a",
            workerId: 1);

        fixture.Store.SetFailed("run-1", "worker crashed");

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Failed, state.Status);
        Assert.Equal("worker crashed", state.Message);
        Assert.Equal(MemberRunLifecycleStatus.Failed, state.MemberStatuses!["Member A"].Status);
        Assert.Equal("idle", state.WorkerStatuses![1].State);
    }

    [Fact]
    public void ExplicitCancellation_CancelsAllMembersAndResetsWorkers()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start();
        fixture.ApplyEvent(
            RunnerStep.QueryStarted,
            memberName: "Member A",
            scriptCode: "script-a",
            workerId: 1);
        fixture.Store.SetCancellationRequested("run-1", "stop requested");

        fixture.Store.SetCancelled("run-1", "cancelled by user");

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Cancelled, state.Status);
        Assert.Equal("cancelled by user", state.Message);
        Assert.Equal(MemberRunLifecycleStatus.Cancelled, state.MemberStatuses!["Member A"].Status);
        Assert.Equal("idle", state.WorkerStatuses![1].State);
    }

    [Fact]
    public void CancellationRequested_IgnoresProgressButAcceptsCompletedEvent()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start(publishToGateway: false);
        fixture.Store.SetCancellationRequested("run-1", "stop requested");

        fixture.ApplyEvent(
            RunnerStep.FileWritten,
            memberName: "Member A",
            filePath: fixture.ArtifactPath());
        var waiting = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.CancellationRequested, waiting.Status);
        Assert.Empty(waiting.OutputArtifacts!);

        fixture.ApplyEvent(RunnerStep.Completed);

        var completed = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Completed, completed.Status);
    }

    [Fact]
    public void TerminalState_IgnoresLaterRunnerEventsAndSetRunning()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start(publishToGateway: false);
        fixture.ApplyEvent(RunnerStep.Completed, message: "done");
        var terminal = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));

        fixture.ApplyEvent(RunnerStep.Failed, message: "late failure");
        fixture.Store.SetRunning("run-1");

        Assert.Same(terminal, fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Completed, terminal.Status);
        Assert.Equal("done", terminal.Message);
    }

    public enum TerminalMutation
    {
        Failed,
        CancellationRequested,
        Cancelled
    }
}
