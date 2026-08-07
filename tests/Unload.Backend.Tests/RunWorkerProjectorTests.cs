using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class RunWorkerProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateInitial_UsesAtLeastOneIdleWorker()
    {
        var result = new RunWorkerProjector(workerCount: 0).CreateInitial(Now);

        var worker = Assert.Single(result).Value;
        Assert.Equal(1, worker.WorkerId);
        Assert.Equal("idle", worker.State);
    }

    [Fact]
    public void Apply_QueryStartedAndCompletedUpdatesWorkerWithoutMutatingSource()
    {
        var projector = new RunWorkerProjector(workerCount: 2);
        var source = projector.CreateInitial(Now);

        var running = projector.Apply(
            source,
            Event(RunnerStep.QueryStarted, workerId: 2),
            Now.AddMinutes(1));
        var idle = projector.Apply(
            running,
            Event(RunnerStep.QueryCompleted, workerId: 2),
            Now.AddMinutes(2));

        Assert.Equal("idle", source[2].State);
        Assert.Equal("running", running[2].State);
        Assert.Equal("script-1", running[2].ScriptCode);
        Assert.Equal("idle", idle[2].State);
        Assert.Null(idle[2].ScriptCode);
    }

    [Fact]
    public void Apply_ExtractsWorkerIdFromLegacyMessage()
    {
        var projector = new RunWorkerProjector(workerCount: 2);

        var result = projector.Apply(
            projector.CreateInitial(Now),
            Event(RunnerStep.QueryStarted, workerId: null, message: "Worker #2 started"),
            Now.AddMinutes(1));

        Assert.Equal("running", result[2].State);
    }

    [Theory]
    [InlineData(RunnerStep.Completed)]
    [InlineData(RunnerStep.Failed)]
    public void Apply_TerminalEventResetsAllWorkers(RunnerStep step)
    {
        var projector = new RunWorkerProjector(workerCount: 1);
        var running = projector.Apply(
            projector.CreateInitial(Now),
            Event(RunnerStep.QueryStarted, workerId: 1),
            Now.AddMinutes(1));

        var result = projector.Apply(running, Event(step), Now.AddMinutes(2));

        Assert.Equal("idle", result[1].State);
        Assert.Null(result[1].MemberName);
        Assert.Null(result[1].ScriptCode);
    }

    private static RunnerEvent Event(RunnerStep step, int? workerId = null, string? message = null)
    {
        return new RunnerEvent(
            Now,
            "run-1",
            step,
            message ?? step.ToString(),
            MemberName: "Member A",
            ScriptCode: "script-1",
            WorkerId: workerId);
    }
}
