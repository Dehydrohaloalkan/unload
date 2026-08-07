using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class RunCompletionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(CompletionScenario.RunnerNotCompleted, RunLifecycleStatus.Running, true)]
    [InlineData(CompletionScenario.GatewayDisabled, RunLifecycleStatus.Completed, false)]
    [InlineData(CompletionScenario.FailedBatch, RunLifecycleStatus.Failed, false)]
    [InlineData(CompletionScenario.NoArtifactsOrBatches, RunLifecycleStatus.Completed, false)]
    [InlineData(CompletionScenario.BatchInProgress, RunLifecycleStatus.Running, true)]
    [InlineData(CompletionScenario.MissingMemberBatch, RunLifecycleStatus.Running, true)]
    [InlineData(CompletionScenario.MissingSentFile, RunLifecycleStatus.Running, true)]
    [InlineData(CompletionScenario.CompletedDelivery, RunLifecycleStatus.Completed, false)]
    [InlineData(CompletionScenario.SkippedDelivery, RunLifecycleStatus.Completed, false)]
    public void Apply_UsesCurrentCompletionTable(
        CompletionScenario scenario,
        RunLifecycleStatus expectedStatus,
        bool expectedSameInstance)
    {
        var current = CreateState(scenario);

        var result = RunCompletionPolicy.Apply(current, Now.AddMinutes(1));

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedSameInstance, ReferenceEquals(current, result));
    }

    [Fact]
    public void GatewayDisabled_CreatesOneSkippedBatchPerDistinctMember()
    {
        var current = CreateState(CompletionScenario.GatewayDisabled) with
        {
            MemberStatuses = new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["Member A"] = Member("Member A"),
                ["Member B"] = Member("Member B")
            },
            OutputArtifacts =
            [
                Artifact("Member A", "/tmp/a.txt"),
                Artifact("member b", "/tmp/b.txt")
            ]
        };

        var result = RunCompletionPolicy.Apply(current, Now.AddMinutes(1));

        Assert.Equal(2, result.SenderBatches!.Count);
        Assert.All(
            result.SenderBatches.Values,
            batch => Assert.Equal(SenderBatchStatus.SkippedByRequest, batch.Status));
    }

    private static RunStatusInfo CreateState(CompletionScenario scenario)
    {
        var artifactPath = Path.GetFullPath("/tmp/result.txt");
        var state = new RunStatusInfo(
            CorrelationId: "run-1",
            TaskCode: "run",
            Status: RunLifecycleStatus.Running,
            TargetCodes: ["TARGET-1"],
            CreatedAt: Now,
            UpdatedAt: Now,
            LastStep: RunnerStep.Completed,
            MemberStatuses: new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["Member A"] = Member("Member A")
            },
            OutputArtifacts: [Artifact("Member A", artifactPath)],
            SenderBatches: new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase),
            PublishToGateway: true);

        return scenario switch
        {
            CompletionScenario.RunnerNotCompleted => state with { LastStep = RunnerStep.FileWritten },
            CompletionScenario.GatewayDisabled => state with { PublishToGateway = false },
            CompletionScenario.FailedBatch => state with
            {
                SenderBatches = Batches(Batch(SenderBatchStatus.Failed, artifactPath))
            },
            CompletionScenario.NoArtifactsOrBatches => state with
            {
                OutputArtifacts = Array.Empty<RunOutputArtifactInfo>()
            },
            CompletionScenario.BatchInProgress => state with
            {
                SenderBatches = Batches(Batch(SenderBatchStatus.InProgress, artifactPath))
            },
            CompletionScenario.MissingMemberBatch => state,
            CompletionScenario.MissingSentFile => state with
            {
                SenderBatches = Batches(Batch(SenderBatchStatus.Completed, sentFilePath: null))
            },
            CompletionScenario.CompletedDelivery => state with
            {
                SenderBatches = Batches(Batch(SenderBatchStatus.Completed, artifactPath))
            },
            CompletionScenario.SkippedDelivery => state with
            {
                SenderBatches = Batches(Batch(SenderBatchStatus.SkippedByRequest, sentFilePath: null))
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private static MemberRunStatusInfo Member(string name)
    {
        return new MemberRunStatusInfo(name, MemberRunLifecycleStatus.Completed, RunnerStep.Completed, "done", Now);
    }

    private static RunOutputArtifactInfo Artifact(string memberName, string path)
    {
        return new RunOutputArtifactInfo(Path.GetFileName(path), path, memberName, "script", Now);
    }

    private static SenderBatchStatusInfo Batch(SenderBatchStatus status, string? sentFilePath)
    {
        return new SenderBatchStatusInfo(
            "batch-1",
            "Member A",
            status,
            Now,
            sentFilePath is null ? [] : [new SenderFileDispatchStateInfo(sentFilePath, Now)]);
    }

    private static IReadOnlyDictionary<string, SenderBatchStatusInfo> Batches(SenderBatchStatusInfo batch)
    {
        return new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [batch.BatchId] = batch
        };
    }

    public enum CompletionScenario
    {
        RunnerNotCompleted,
        GatewayDisabled,
        FailedBatch,
        NoArtifactsOrBatches,
        BatchInProgress,
        MissingMemberBatch,
        MissingSentFile,
        CompletedDelivery,
        SkippedDelivery
    }
}
