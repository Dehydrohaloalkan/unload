using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class RunStateStoreGatewayCompletionTests
{
    [Fact]
    public void RunnerCompletedWithArtifact_RemainsRunningUntilSenderFeedbackIsComplete()
    {
        using var fixture = CompletedRunnerWithArtifact();

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));

        Assert.Equal(RunLifecycleStatus.Running, state.Status);
        Assert.Equal(RunnerStep.Completed, state.LastStep);
    }

    [Fact]
    public void FileSentWithoutBatchCompleted_RemainsRunning()
    {
        using var fixture = CompletedRunnerWithArtifact();

        fixture.ApplyFeedback(SenderFeedbackKind.FileSent, filePath: fixture.ArtifactPath());

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Running, state.Status);
        Assert.Equal(SenderBatchStatus.InProgress, state.SenderBatches!["batch-1"].Status);
    }

    [Fact]
    public void BatchCompletedWithoutFileSent_RemainsRunning()
    {
        using var fixture = CompletedRunnerWithArtifact();

        fixture.ApplyFeedback(SenderFeedbackKind.BatchCompleted);

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Running, state.Status);
        Assert.Equal(SenderBatchStatus.Completed, state.SenderBatches!["batch-1"].Status);
        Assert.Empty(state.SenderBatches["batch-1"].SentFiles);
    }

    [Fact]
    public void FileSentAndBatchCompleted_PromoteRunToCompleted()
    {
        using var fixture = CompletedRunnerWithArtifact();
        fixture.ApplyFeedback(SenderFeedbackKind.FileSent, filePath: fixture.ArtifactPath());

        fixture.ApplyFeedback(SenderFeedbackKind.BatchCompleted);

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Completed, state.Status);
        var batch = state.SenderBatches!["batch-1"];
        Assert.Equal(SenderBatchStatus.Completed, batch.Status);
        Assert.Single(batch.SentFiles);
        Assert.Equal(Path.GetFullPath(fixture.ArtifactPath()), Assert.Single(batch.SentFiles).FilePath);
    }

    [Fact]
    public void DuplicateFileSentFeedback_IsIdempotent()
    {
        using var fixture = CompletedRunnerWithArtifact();
        fixture.ApplyFeedback(SenderFeedbackKind.FileSent, filePath: fixture.ArtifactPath());

        fixture.ApplyFeedback(SenderFeedbackKind.FileSent, filePath: fixture.ArtifactPath());

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Single(state.SenderBatches!["batch-1"].SentFiles);
    }

    [Fact]
    public void FailedBatchAfterRunnerCompleted_PromotesRunToFailed()
    {
        using var fixture = CompletedRunnerWithArtifact();

        fixture.ApplyFeedback(SenderFeedbackKind.BatchFailed, message: "ftp failed");

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get("run-1"));
        Assert.Equal(RunLifecycleStatus.Failed, state.Status);
        Assert.Equal("Sender batch failed.", state.Message);
        Assert.Equal(SenderBatchStatus.Failed, state.SenderBatches!["batch-1"].Status);
    }

    [Fact]
    public void MultipleArtifacts_RequireCompletedFeedbackForEveryMember()
    {
        using var fixture = new RunStateStoreFixture();
        var firstPath = fixture.ArtifactPath("first.txt");
        var secondPath = fixture.ArtifactPath("second.txt");
        fixture.Start(members: ["Member A", "Member B"]);
        fixture.ApplyEvent(RunnerStep.FileWritten, memberName: "Member A", filePath: firstPath);
        fixture.ApplyEvent(RunnerStep.FileWritten, memberName: "Member B", filePath: secondPath);
        fixture.ApplyEvent(RunnerStep.Completed);
        fixture.ApplyFeedback(SenderFeedbackKind.FileSent, memberName: "Member A", batchId: "batch-a", filePath: firstPath);
        fixture.ApplyFeedback(SenderFeedbackKind.BatchCompleted, memberName: "Member A", batchId: "batch-a");

        Assert.Equal(RunLifecycleStatus.Running, fixture.Store.Get("run-1")!.Status);

        fixture.ApplyFeedback(SenderFeedbackKind.FileSent, memberName: "Member B", batchId: "batch-b", filePath: secondPath);
        fixture.ApplyFeedback(SenderFeedbackKind.BatchCompleted, memberName: "Member B", batchId: "batch-b");

        Assert.Equal(RunLifecycleStatus.Completed, fixture.Store.Get("run-1")!.Status);
    }

    [Fact]
    public void RunnerCompletedWithoutArtifacts_CompletesWithoutSenderFeedback()
    {
        using var fixture = new RunStateStoreFixture();
        fixture.Start();

        fixture.ApplyEvent(RunnerStep.Completed);

        Assert.Equal(RunLifecycleStatus.Completed, fixture.Store.Get("run-1")!.Status);
    }

    [Theory]
    [InlineData("extra-123", "extra")]
    [InlineData("preset-123", "preset")]
    [InlineData("run-123", "run")]
    public void FeedbackForUnknownCorrelation_CreatesStateWithResolvedTaskCode(
        string correlationId,
        string expectedTaskCode)
    {
        using var fixture = new RunStateStoreFixture();

        fixture.ApplyFeedback(SenderFeedbackKind.BatchCompleted, correlationId: correlationId);

        var state = Assert.IsType<RunStatusInfo>(fixture.Store.Get(correlationId));
        Assert.Equal(expectedTaskCode, state.TaskCode);
        Assert.Equal(RunLifecycleStatus.Running, state.Status);
        Assert.Equal(SenderBatchStatus.Completed, state.SenderBatches!["batch-1"].Status);
    }

    private static RunStateStoreFixture CompletedRunnerWithArtifact()
    {
        var fixture = new RunStateStoreFixture();
        fixture.Start();
        fixture.ApplyEvent(
            RunnerStep.FileWritten,
            memberName: "Member A",
            scriptCode: "script-a",
            filePath: fixture.ArtifactPath());
        fixture.ApplyEvent(RunnerStep.Completed);
        return fixture;
    }
}
