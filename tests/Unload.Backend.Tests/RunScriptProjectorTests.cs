using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class RunScriptProjectorTests
{
    private static readonly DateTimeOffset DiscoveredAt = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Apply_ScriptDiscoveredCreatesOneCaseInsensitiveStableCard()
    {
        var upper = Event(RunnerStep.ScriptDiscovered, memberName: "Member A", scriptCode: "SCRIPT-1");
        var lower = Event(RunnerStep.ScriptDiscovered, memberName: "member a", scriptCode: "script-1");

        var result = RunScriptProjector.Apply(
            RunScriptProjector.Apply(source: null, upper),
            lower);

        Assert.Single(result);
        Assert.Equal(
            RunScriptProjector.CreateId("Member A", "SCRIPT-1"),
            RunScriptProjector.CreateId("member a", "script-1"));
        var card = result[RunScriptProjector.CreateId("Member A", "SCRIPT-1")];
        Assert.Equal(ScriptRunStage.AwaitingWorker, card.Stage);
        Assert.Equal(DiscoveredAt, card.DiscoveredAt);
        Assert.Equal(DiscoveredAt, card.StageEnteredAt);
        Assert.Equal("discovered", card.Message);
    }

    [Fact]
    public void Apply_QueryStartedUsesStructuredWorkerAndPreservesSource()
    {
        var discovered = RunScriptProjector.Apply(
            source: null,
            Event(RunnerStep.ScriptDiscovered));

        var result = RunScriptProjector.Apply(
            discovered,
            Event(RunnerStep.QueryStarted, occurredAt: DiscoveredAt.AddSeconds(12), workerId: 3, message: "running"));

        var id = RunScriptProjector.CreateId("Member A", "SCRIPT-1");
        Assert.Equal(ScriptRunStage.AwaitingWorker, discovered[id].Stage);
        Assert.Equal(ScriptRunStage.Running, result[id].Stage);
        Assert.Equal(3, result[id].WorkerId);
        Assert.Equal(DiscoveredAt.AddSeconds(12), result[id].StartedAt);
        Assert.Equal(DiscoveredAt.AddSeconds(12), result[id].StageEnteredAt);
    }

    [Fact]
    public void Apply_QueryCompletedSetsRecordsAndKeepsWorker()
    {
        var running = ApplyThroughStarted();

        var result = RunScriptProjector.Apply(
            running,
            Event(
                RunnerStep.QueryCompleted,
                occurredAt: DiscoveredAt.AddMinutes(2),
                records: 128_450,
                message: "completed"));

        var card = Assert.Single(result).Value;
        Assert.Equal(ScriptRunStage.Completed, card.Stage);
        Assert.Equal(3, card.WorkerId);
        Assert.Equal(128_450, card.Records);
        Assert.Equal(DiscoveredAt.AddMinutes(2), card.CompletedAt);
        Assert.Equal(DiscoveredAt.AddMinutes(2), card.StageEnteredAt);
    }

    [Fact]
    public void Apply_GlobalFailureMarksOnlyUnfinishedCardsFailed()
    {
        var completed = ApplyThroughStarted();
        completed = RunScriptProjector.Apply(
            completed,
            Event(RunnerStep.QueryCompleted, occurredAt: DiscoveredAt.AddMinutes(1), records: 4));
        var withWaiting = RunScriptProjector.Apply(
            completed,
            Event(RunnerStep.ScriptDiscovered, memberName: "Member B", scriptCode: "SCRIPT-2"));

        var result = RunScriptProjector.Apply(
            withWaiting,
            new RunnerEvent(
                DiscoveredAt.AddMinutes(2),
                "run-1",
                RunnerStep.Failed,
                "database failed"));

        Assert.Equal(ScriptRunStage.Completed, result[RunScriptProjector.CreateId("Member A", "SCRIPT-1")].Stage);
        var failed = result[RunScriptProjector.CreateId("Member B", "SCRIPT-2")];
        Assert.Equal(ScriptRunStage.Failed, failed.Stage);
        Assert.Equal(DiscoveredAt.AddMinutes(2), failed.CompletedAt);
        Assert.Equal("database failed", failed.Message);
    }

    [Fact]
    public void CancelUnfinishedMarksOnlyUnfinishedCardsCancelled()
    {
        var completed = ApplyThroughStarted();
        completed = RunScriptProjector.Apply(
            completed,
            Event(RunnerStep.QueryCompleted, occurredAt: DiscoveredAt.AddMinutes(1)));
        var withWaiting = RunScriptProjector.Apply(
            completed,
            Event(RunnerStep.ScriptDiscovered, memberName: "Member B", scriptCode: "SCRIPT-2"));

        var result = RunScriptProjector.CancelUnfinished(
            withWaiting,
            "cancelled by user",
            DiscoveredAt.AddMinutes(2));

        Assert.Equal(ScriptRunStage.Completed, result[RunScriptProjector.CreateId("Member A", "SCRIPT-1")].Stage);
        var cancelled = result[RunScriptProjector.CreateId("Member B", "SCRIPT-2")];
        Assert.Equal(ScriptRunStage.Cancelled, cancelled.Stage);
        Assert.Equal(DiscoveredAt.AddMinutes(2), cancelled.CompletedAt);
    }

    [Fact]
    public void Apply_DuplicateDiscoveryAndRepeatedStageDoNotResetStageEnteredAt()
    {
        var discoveredEvent = Event(RunnerStep.ScriptDiscovered);
        var discovered = RunScriptProjector.Apply(source: null, discoveredEvent);

        var duplicate = RunScriptProjector.Apply(discovered, discoveredEvent);
        var repeatedStart = RunScriptProjector.Apply(
            discovered,
            Event(RunnerStep.QueryStarted, occurredAt: DiscoveredAt.AddSeconds(10), workerId: 2));
        repeatedStart = RunScriptProjector.Apply(
            repeatedStart,
            Event(RunnerStep.QueryStarted, occurredAt: DiscoveredAt.AddMinutes(1), workerId: 3, message: "still running"));

        var id = RunScriptProjector.CreateId("Member A", "SCRIPT-1");
        Assert.Same(discovered[id], duplicate[id]);
        Assert.Equal(ScriptRunStage.Running, repeatedStart[id].Stage);
        Assert.Equal(DiscoveredAt.AddSeconds(10), repeatedStart[id].StageEnteredAt);
        Assert.Equal(3, repeatedStart[id].WorkerId);
    }

    [Fact]
    public void Apply_LateNonTerminalEventDoesNotReopenTerminalCard()
    {
        var completed = RunScriptProjector.Apply(
            ApplyThroughStarted(),
            Event(RunnerStep.QueryCompleted, occurredAt: DiscoveredAt.AddMinutes(1), records: 5));

        var result = RunScriptProjector.Apply(
            completed,
            Event(RunnerStep.QueryStarted, occurredAt: DiscoveredAt.AddMinutes(2), workerId: 4));

        var card = Assert.Single(result).Value;
        Assert.Equal(ScriptRunStage.Completed, card.Stage);
        Assert.Equal(DiscoveredAt.AddMinutes(1), card.StageEnteredAt);
        Assert.Equal(3, card.WorkerId);
    }

    private static IReadOnlyDictionary<string, ScriptRunStatusInfo> ApplyThroughStarted()
    {
        var discovered = RunScriptProjector.Apply(source: null, Event(RunnerStep.ScriptDiscovered));
        return RunScriptProjector.Apply(
            discovered,
            Event(RunnerStep.QueryStarted, occurredAt: DiscoveredAt.AddSeconds(10), workerId: 3));
    }

    private static RunnerEvent Event(
        RunnerStep step,
        string memberName = "Member A",
        string scriptCode = "SCRIPT-1",
        DateTimeOffset? occurredAt = null,
        int? workerId = null,
        int? records = null,
        string? message = null)
    {
        return new RunnerEvent(
            occurredAt ?? DiscoveredAt,
            "run-1",
            step,
            message ?? (step == RunnerStep.ScriptDiscovered ? "discovered" : step.ToString()),
            memberName,
            scriptCode,
            records,
            WorkerId: workerId);
    }
}
