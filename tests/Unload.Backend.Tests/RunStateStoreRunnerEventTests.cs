using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class RunStateStoreRunnerEventTests
{
    [Fact]
    public void MemberLifecycle_TracksPendingRunningAndCompleted()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start(members: ["Member A"]);
        Assert.Equal(MemberRunLifecycleStatus.Pending, Member(fixture).Status);

        fixture.ApplyEvent(RunnerStep.QueryStarted, memberName: "Member A", scriptCode: "script-a");
        Assert.Equal(MemberRunLifecycleStatus.Running, Member(fixture).Status);

        fixture.ApplyEvent(RunnerStep.ScriptCompleted, memberName: "Member A", scriptCode: "script-b");
        Assert.Equal(MemberRunLifecycleStatus.Completed, Member(fixture).Status);
    }

    [Fact]
    public void FileWritten_AddsArtifactWithoutDuplicate()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start();
        var path = fixture.ArtifactPath();

        fixture.ApplyEvent(RunnerStep.FileWritten, memberName: "Member A", scriptCode: "script-a", filePath: path);
        fixture.ApplyEvent(RunnerStep.FileWritten, memberName: "Member A", scriptCode: "script-a", filePath: path);

        var artifact = Assert.Single(fixture.Store.Get("run-1")!.OutputArtifacts!);
        Assert.Equal(path, artifact.FilePath);
        Assert.Equal("Member A", artifact.MemberName);
        Assert.Equal("script-a", artifact.ScriptCode);
    }

    [Fact]
    public void ScopedFailure_PreservesUsefulContext()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start();
        var failure = new RunnerFailureInfo(
            "query", "script", "Member A:script-a", "Member A", "script-a",
            null, null, null, null, "RUNNER_QUERY_FAILED", "Member A / script-a failed during query.",
            DateTimeOffset.UtcNow);

        fixture.Store.ApplyEvent(new RunnerEvent(
            DateTimeOffset.UtcNow, "run-1", RunnerStep.Failed, failure.Message,
            MemberName: "Member A", ScriptCode: "script-a", Failure: failure));

        var state = fixture.Store.Get("run-1")!;
        Assert.Equal(RunLifecycleStatus.Failed, state.Status);
        Assert.Equal("query", state.Failure!.Stage);
        Assert.Equal("Member A", state.Failure.MemberName);
        Assert.Equal("script-a", state.Failure.ScriptCode);
        Assert.Equal("Query execution failed for script 'script-a'. Review the script and database logs.", state.Failure.Message);
        Assert.Equal(state.Failure, state.MemberStatuses!["Member A"].Failure);
    }

    [Theory]
    [InlineData(TerminalMutation.AggregateFailure)]
    [InlineData(TerminalMutation.SetFailed)]
    [InlineData(TerminalMutation.SetCancelled)]
    public void TerminalMutation_PreservesCompletedMembers(TerminalMutation mutation)
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start(members: ["Member A", "Member B"]);
        fixture.ApplyEvent(RunnerStep.ScriptCompleted, memberName: "Member A", scriptCode: "done");

        switch (mutation)
        {
            case TerminalMutation.AggregateFailure:
                fixture.ApplyEvent(RunnerStep.Failed, message: "run failed");
                break;
            case TerminalMutation.SetFailed:
                fixture.Store.SetFailed("run-1", "run failed");
                break;
            case TerminalMutation.SetCancelled:
                fixture.Store.SetCancelled("run-1", "run cancelled");
                break;
        }

        var members = fixture.Store.Get("run-1")!.MemberStatuses!;
        Assert.Equal(MemberRunLifecycleStatus.Completed, members["Member A"].Status);
        Assert.Equal(
            mutation == TerminalMutation.SetCancelled
                ? MemberRunLifecycleStatus.Cancelled
                : MemberRunLifecycleStatus.Failed,
            members["Member B"].Status);
    }

    [Fact]
    public void CompletedWithoutGateway_CompletesRunAndMembers()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start(publishToGateway: false, members: ["Member A", "Member B"]);

        fixture.ApplyEvent(RunnerStep.Completed, filePath: fixture.ScratchDirectory);

        var state = fixture.Store.Get("run-1")!;
        Assert.Equal(RunLifecycleStatus.Completed, state.Status);
        Assert.All(state.MemberStatuses!.Values, member =>
            Assert.Equal(MemberRunLifecycleStatus.Completed, member.Status));
        Assert.All(state.SenderBatches!.Values, batch =>
            Assert.Equal(SenderBatchStatus.SkippedByRequest, batch.Status));
    }

    [Fact]
    public void TerminalState_IgnoresLaterRunnerEvents()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start(publishToGateway: false);
        fixture.ApplyEvent(RunnerStep.Completed, message: "done");
        var terminal = fixture.Store.Get("run-1");

        fixture.ApplyEvent(RunnerStep.Failed, message: "late failure");

        Assert.Same(terminal, fixture.Store.Get("run-1"));
    }

    private static MemberRunStatusInfo Member(RunStateStoreFixture fixture) =>
        fixture.Store.Get("run-1")!.MemberStatuses!["Member A"];

    public enum TerminalMutation
    {
        AggregateFailure,
        SetFailed,
        SetCancelled
    }
}
