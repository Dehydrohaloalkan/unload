using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class GatewayFeedbackProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(SenderFeedbackKind.FileSent, SenderBatchStatus.InProgress)]
    [InlineData(SenderFeedbackKind.BatchCompleted, SenderBatchStatus.Completed)]
    [InlineData(SenderFeedbackKind.BatchFailed, SenderBatchStatus.Failed)]
    public void Apply_MapsFeedbackKindToBatchStatus(
        SenderFeedbackKind kind,
        SenderBatchStatus expectedStatus)
    {
        var feedback = Feedback(kind, filePath: kind == SenderFeedbackKind.FileSent ? "/tmp/a.txt" : null);

        var result = GatewayFeedbackProjector.Apply(source: null, feedback, Now.AddMinutes(1));

        var batch = Assert.Single(result).Value;
        Assert.Equal(expectedStatus, batch.Status);
        Assert.Equal("batch-1", batch.BatchId);
        Assert.Equal("Member A", batch.MemberName);
    }

    [Fact]
    public void FileSent_NormalizesAndSortsPaths()
    {
        var source = Batches(new SenderBatchStatusInfo(
            "batch-1",
            "Member A",
            SenderBatchStatus.InProgress,
            Now,
            [new SenderFileDispatchStateInfo(Path.GetFullPath("/tmp/z.txt"), Now)]));

        var result = GatewayFeedbackProjector.Apply(
            source,
            Feedback(SenderFeedbackKind.FileSent, filePath: " /tmp/a.txt "),
            Now.AddMinutes(1));

        Assert.Equal(
            [Path.GetFullPath("/tmp/a.txt"), Path.GetFullPath("/tmp/z.txt")],
            result["batch-1"].SentFiles.Select(static file => file.FilePath));
    }

    [Fact]
    public void DuplicateFileSent_IsIdempotentAndCaseInsensitive()
    {
        var path = Path.GetFullPath("/tmp/result.txt");
        var source = Batches(new SenderBatchStatusInfo(
            "batch-1",
            "Member A",
            SenderBatchStatus.InProgress,
            Now,
            [new SenderFileDispatchStateInfo(path.ToUpperInvariant(), Now)]));

        var result = GatewayFeedbackProjector.Apply(
            source,
            Feedback(SenderFeedbackKind.FileSent, filePath: path),
            Now.AddMinutes(1));

        Assert.Single(result["batch-1"].SentFiles);
    }

    [Fact]
    public void TerminalBatchFeedback_PreservesPreviouslySentFiles()
    {
        var path = Path.GetFullPath("/tmp/result.txt");
        var source = Batches(new SenderBatchStatusInfo(
            "batch-1",
            "Member A",
            SenderBatchStatus.InProgress,
            Now,
            [new SenderFileDispatchStateInfo(path, Now)]));

        var result = GatewayFeedbackProjector.Apply(
            source,
            Feedback(SenderFeedbackKind.BatchCompleted, message: "sent"),
            Now.AddMinutes(1));

        var batch = result["batch-1"];
        Assert.Equal(SenderBatchStatus.Completed, batch.Status);
        Assert.Equal("sent", batch.Message);
        Assert.Equal(path, Assert.Single(batch.SentFiles).FilePath);
    }

    [Fact]
    public void Apply_PreservesOtherBatchesAndDoesNotMutateSource()
    {
        var existing = new SenderBatchStatusInfo(
            "other-batch",
            "Member B",
            SenderBatchStatus.Completed,
            Now,
            []);
        var source = Batches(existing);

        var result = GatewayFeedbackProjector.Apply(
            source,
            Feedback(SenderFeedbackKind.BatchFailed, message: "failed"),
            Now.AddMinutes(1));

        Assert.Single(source);
        Assert.Equal(2, result.Count);
        Assert.Same(existing, result["other-batch"]);
        Assert.Equal(SenderBatchStatus.Failed, result["batch-1"].Status);
    }

    private static SenderFileDispatchFeedback Feedback(
        SenderFeedbackKind kind,
        string? filePath = null,
        string? message = null)
    {
        return new SenderFileDispatchFeedback(
            Now,
            "run-1",
            "Member A",
            "batch-1",
            kind,
            filePath,
            message);
    }

    private static IReadOnlyDictionary<string, SenderBatchStatusInfo> Batches(SenderBatchStatusInfo batch)
    {
        return new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [batch.BatchId] = batch
        };
    }
}
