using System.Text.Json;
using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class RunStateStorePersistenceTests
{
    [Fact]
    public void MissingSnapshot_StartsWithEmptyState()
    {
        using var fixture = new RunStateStoreFixture();

        Assert.False(File.Exists(fixture.StateFilePath));
        Assert.Empty(fixture.Store.List());
    }

    [Fact]
    public void CorruptedSnapshot_StartsWithEmptyStateWithoutOverwritingFile()
    {
        using var fixture = new RunStateStoreFixture();
        const string corruptedJson = "{ this is not valid json";
        File.WriteAllText(fixture.StateFilePath, corruptedJson);

        fixture.Restart();

        Assert.Empty(fixture.Store.List());
        Assert.Equal(corruptedJson, File.ReadAllText(fixture.StateFilePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveStateAfterRestart_BecomesCancelledAndResetsWorkers(bool cancellationRequested)
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start();
        fixture.ApplyEvent(
            RunnerStep.QueryStarted,
            memberName: "Member A",
            scriptCode: "script-a",
            workerId: 1);
        if (cancellationRequested)
        {
            fixture.Store.SetCancellationRequested("run-1", "stop requested");
        }

        fixture.Restart();

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Cancelled, state.Status);
        Assert.Equal(RunnerStep.Failed, state.LastStep);
        Assert.Equal("Run was interrupted due to server restart.", state.Message);
        Assert.Equal("idle", state.WorkerStatuses![1].State);
        Assert.Null(state.WorkerStatuses[1].ScriptCode);
        Assert.Null(state.WorkerStatuses[1].MemberName);
    }

    [Theory]
    [InlineData(RunLifecycleStatus.Completed)]
    [InlineData(RunLifecycleStatus.Failed)]
    [InlineData(RunLifecycleStatus.Cancelled)]
    public void TerminalStateAfterRestart_IsPreserved(RunLifecycleStatus terminalStatus)
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start(publishToGateway: false);
        switch (terminalStatus)
        {
            case RunLifecycleStatus.Completed:
                fixture.ApplyEvent(RunnerStep.Completed, message: "completed");
                break;
            case RunLifecycleStatus.Failed:
                fixture.Store.SetFailed("run-1", "failed");
                break;
            case RunLifecycleStatus.Cancelled:
                fixture.Store.SetCancelled("run-1", "cancelled");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(terminalStatus));
        }

        var beforeRestart = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        fixture.Restart();
        var afterRestart = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));

        Assert.Equal(beforeRestart.CorrelationId, afterRestart.CorrelationId);
        Assert.Equal(beforeRestart.TaskCode, afterRestart.TaskCode);
        Assert.Equal(beforeRestart.Status, afterRestart.Status);
        Assert.Equal(beforeRestart.TargetCodes, afterRestart.TargetCodes);
        Assert.Equal(beforeRestart.CreatedAt, afterRestart.CreatedAt);
        Assert.Equal(beforeRestart.UpdatedAt, afterRestart.UpdatedAt);
        Assert.Equal(beforeRestart.LastStep, afterRestart.LastStep);
        Assert.Equal(beforeRestart.Message, afterRestart.Message);
        Assert.Equal(beforeRestart.OutputPath, afterRestart.OutputPath);
        Assert.Equal(beforeRestart.PublishToGateway, afterRestart.PublishToGateway);
        Assert.Equal(beforeRestart.MemberStatuses, afterRestart.MemberStatuses);
        Assert.Equal(beforeRestart.OutputArtifacts, afterRestart.OutputArtifacts);
        Assert.Equal(beforeRestart.WorkerStatuses, afterRestart.WorkerStatuses);
        Assert.Equal(beforeRestart.SenderBatches!.Count, afterRestart.SenderBatches!.Count);
        foreach (var (batchId, expectedBatch) in beforeRestart.SenderBatches)
        {
            var actualBatch = afterRestart.SenderBatches[batchId];
            Assert.Equal(expectedBatch.BatchId, actualBatch.BatchId);
            Assert.Equal(expectedBatch.MemberName, actualBatch.MemberName);
            Assert.Equal(expectedBatch.Status, actualBatch.Status);
            Assert.Equal(expectedBatch.UpdatedAt, actualBatch.UpdatedAt);
            Assert.Equal(expectedBatch.Message, actualBatch.Message);
            Assert.Equal(expectedBatch.SentFiles, actualBatch.SentFiles);
        }
    }

    [Fact]
    public void PersistedSnapshot_ContainsVersionAndRunState()
    {
        using var fixture = new RunStateStoreFixture();

        fixture.Start(taskCode: "extra", publishToGateway: false);

        using var document = JsonDocument.Parse(File.ReadAllText(fixture.StateFilePath));
        var root = document.RootElement;
        Assert.Equal("1", root.GetProperty("Version").GetString());
        var run = Assert.Single(root.GetProperty("Runs").EnumerateArray());
        Assert.Equal("run-1", run.GetProperty("CorrelationId").GetString());
        Assert.Equal("extra", run.GetProperty("TaskCode").GetString());
        Assert.False(run.GetProperty("PublishToGateway").GetBoolean());
    }
}
