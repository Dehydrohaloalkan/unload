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
    [InlineData(SenderFeedbackKind.BatchStarted, SenderBatchStatus.InProgress)]
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

    [Fact]
    public void Queued_CreatesReadyBatch()
    {
        var queued = QueueEvent();

        var result = GatewayFeedbackProjector.ApplyQueued(source: null, queued, Now.AddMinutes(1));

        var batch = Assert.Single(result).Value;
        Assert.Equal(SenderBatchStatus.Ready, batch.Status);
        Assert.Equal("Member A", batch.MemberName);
        Assert.Empty(batch.SentFiles);
    }

    [Fact]
    public void Started_PromotesQueuedBatch()
    {
        var queued = GatewayFeedbackProjector.ApplyQueued(source: null, QueueEvent(), Now);
        var startedAt = Now.AddMinutes(1);

        var result = GatewayFeedbackProjector.Apply(
            queued,
            Feedback(SenderFeedbackKind.BatchStarted, occurredAt: startedAt),
            startedAt);

        var batch = result["batch-1"];
        Assert.Equal(SenderBatchStatus.InProgress, batch.Status);
        Assert.Equal(startedAt, batch.UpdatedAt);
    }

    [Fact]
    public void LateQueuedEvent_EnrichesStartedBatchWithoutRegression()
    {
        var startedAt = Now.AddMinutes(1);
        var started = GatewayFeedbackProjector.Apply(
            source: null,
            Feedback(SenderFeedbackKind.BatchStarted, occurredAt: startedAt),
            startedAt);

        var result = GatewayFeedbackProjector.ApplyQueued(started, QueueEvent(), Now.AddMinutes(2));

        var batch = result["batch-1"];
        Assert.Equal(SenderBatchStatus.InProgress, batch.Status);
        Assert.Equal(startedAt, batch.UpdatedAt);
    }

    [Fact]
    public void FileSentAfterStarted_PreservesStartAndAddsSentFile()
    {
        var startedAt = Now.AddMinutes(1);
        var started = GatewayFeedbackProjector.Apply(
            source: null,
            Feedback(SenderFeedbackKind.BatchStarted, occurredAt: startedAt),
            startedAt);

        var result = GatewayFeedbackProjector.Apply(
            started,
            Feedback(SenderFeedbackKind.FileSent, "/tmp/a.txt", occurredAt: Now.AddMinutes(2)),
            Now.AddMinutes(2));

        var batch = result["batch-1"];
        Assert.Equal(SenderBatchStatus.InProgress, batch.Status);
        Assert.Single(batch.SentFiles);
    }

    [Fact]
    public void FileSentAfterQueue_IsIdempotent()
    {
        var queued = GatewayFeedbackProjector.ApplyQueued(null, QueueEvent(), Now);
        var sentAt = Now.AddMinutes(1);

        var once = GatewayFeedbackProjector.Apply(
            queued,
            Feedback(SenderFeedbackKind.FileSent, " /tmp/a.txt ", occurredAt: sentAt),
            sentAt);
        var twice = GatewayFeedbackProjector.Apply(
            once,
            Feedback(SenderFeedbackKind.FileSent, "/TMP/A.TXT", occurredAt: sentAt.AddMinutes(1)),
            sentAt.AddMinutes(1));

        var batch = twice["batch-1"];
        Assert.Single(batch.SentFiles);
        Assert.Equal(sentAt, batch.SentFiles.Single().SentAt);
    }

    [Fact]
    public void FeedbackBeforeQueue_PreservesSentFileAndQueueReplacesFeedbackMemberName()
    {
        var feedback = GatewayFeedbackProjector.Apply(
            null,
            Feedback(
                SenderFeedbackKind.FileSent,
                "/tmp/a.txt",
                occurredAt: Now.AddMinutes(1),
                memberName: "wrong feedback"),
            Now.AddMinutes(1));

        var result = GatewayFeedbackProjector.ApplyQueued(
            feedback,
            QueueEvent(memberName: "Authoritative Member"),
            Now.AddMinutes(2));

        var batch = result["batch-1"];
        Assert.Equal("Authoritative Member", batch.MemberName);
        Assert.Equal(Now.AddMinutes(1), Assert.Single(batch.SentFiles).SentAt);
    }

    [Fact]
    public void QueueBeforeMismatchedFeedback_PreservesAuthoritativeMemberName()
    {
        var queued = GatewayFeedbackProjector.ApplyQueued(
            null,
            QueueEvent(memberName: "Authoritative Member"),
            Now);

        var result = GatewayFeedbackProjector.Apply(
            queued,
            Feedback(SenderFeedbackKind.BatchStarted, memberName: "wrong feedback"),
            Now.AddMinutes(1));

        Assert.Equal("Authoritative Member", result["batch-1"].MemberName);
    }

    [Theory]
    [InlineData(SenderFeedbackKind.BatchCompleted, SenderBatchStatus.Completed)]
    [InlineData(SenderFeedbackKind.BatchFailed, SenderBatchStatus.Failed)]
    public void LateQueuedEvent_DoesNotRegressTerminalBatch(
        SenderFeedbackKind terminalKind,
        SenderBatchStatus expectedStatus)
    {
        var terminal = GatewayFeedbackProjector.Apply(
            source: null,
            Feedback(terminalKind),
            Now.AddMinutes(1));

        var result = GatewayFeedbackProjector.ApplyQueued(terminal, QueueEvent(), Now.AddMinutes(2));

        var batch = result["batch-1"];
        Assert.Equal(expectedStatus, batch.Status);
        Assert.Equal(Now.AddMinutes(1), batch.UpdatedAt);
    }

    [Fact]
    public void LateQueuedEvent_PreservesTerminalSentFilesAndFailure()
    {
        var path = Path.GetFullPath("/tmp/sent.txt");
        var failure = new RunnerFailureInfo(
            "sender",
            "batch",
            "batch-1",
            "Feedback Member",
            null,
            null,
            null,
            path,
            "batch-1",
            "SENDER_FAILED",
            "Gateway sender failed.",
            Now);
        var terminal = Batches(new SenderBatchStatusInfo(
            "batch-1",
            "Feedback Member",
            SenderBatchStatus.Failed,
            Now.AddMinutes(1),
            [new SenderFileDispatchStateInfo(path, Now)],
            Message: "failed",
            Failure: failure));

        var result = GatewayFeedbackProjector.ApplyQueued(
            terminal,
            QueueEvent(memberName: "Authoritative Member"),
            Now.AddMinutes(2));

        var batch = result["batch-1"];
        Assert.Equal(SenderBatchStatus.Failed, batch.Status);
        Assert.Equal(Now.AddMinutes(1), batch.UpdatedAt);
        Assert.Equal("Authoritative Member", batch.MemberName);
        Assert.Equal(path, Assert.Single(batch.SentFiles).FilePath);
        Assert.Same(failure, batch.Failure);
    }

    [Fact]
    public void LateFeedback_DoesNotMutateTerminalBatch()
    {
        var sentPath = Path.GetFullPath("/tmp/sent.txt");
        var terminal = Batches(new SenderBatchStatusInfo(
            "batch-1",
            "Member A",
            SenderBatchStatus.Completed,
            Now,
            [new SenderFileDispatchStateInfo(sentPath, Now)],
            Message: "completed"));

        var result = GatewayFeedbackProjector.Apply(
            terminal,
            Feedback(SenderFeedbackKind.BatchStarted, message: "late start", occurredAt: Now.AddMinutes(1)),
            Now.AddMinutes(1));

        var batch = result["batch-1"];
        Assert.Equal(SenderBatchStatus.Completed, batch.Status);
        Assert.Equal(Now, batch.UpdatedAt);
        Assert.Equal("completed", batch.Message);
        Assert.Equal(sentPath, Assert.Single(batch.SentFiles).FilePath);
    }

    [Fact]
    public void FailedFeedback_DoesNotPublishRawExceptionMessage()
    {
        const string sentinel = "SQL password=sentinel; /srv/internal/ftp/path";

        var result = GatewayFeedbackProjector.Apply(
            source: null,
            Feedback(SenderFeedbackKind.BatchFailed, message: sentinel),
            Now.AddMinutes(1));

        var batch = result["batch-1"];
        Assert.DoesNotContain(sentinel, batch.Message, StringComparison.Ordinal);
        Assert.Contains("Gateway sender failed", batch.Message, StringComparison.Ordinal);
    }

    private static SenderFileDispatchFeedback Feedback(
        SenderFeedbackKind kind,
        string? filePath = null,
        string? message = null,
        DateTimeOffset? occurredAt = null,
        string memberName = "Member A")
    {
        return new SenderFileDispatchFeedback(
            occurredAt ?? Now,
            "run-1",
            memberName,
            "batch-1",
            kind,
            filePath,
            message);
    }

    private static RunnerEvent QueueEvent(string memberName = "Member A") => new(
        Now,
        "run-1",
        RunnerStep.GatewayBatchQueued,
        "Gateway batch queued.",
        MemberName: memberName,
        BatchId: "batch-1");

    private static IReadOnlyDictionary<string, SenderBatchStatusInfo> Batches(SenderBatchStatusInfo batch)
    {
        return new Dictionary<string, SenderBatchStatusInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [batch.BatchId] = batch
        };
    }
}
