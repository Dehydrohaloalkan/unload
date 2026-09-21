using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class RunFileProjectorTests
{
    private static readonly DateTimeOffset QueuedAt = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Apply_WriteStartedThenWritten_ProjectsLinkedFileCard()
    {
        var queued = Event(RunnerStep.FileWriteStarted, occurredAt: QueuedAt, memberName: "Member A", scriptCode: "SCRIPT-1", chunkNumber: 3, records: 7, estimatedBytes: 123, workerId: 2);
        var writtenAt = QueuedAt.AddSeconds(4);
        var result = RunFileProjector.Apply(
            RunFileProjector.Apply(source: null, queued),
            Event(RunnerStep.FileWritten, occurredAt: writtenAt, memberName: "Member A", scriptCode: "SCRIPT-1", chunkNumber: 3, records: 7, estimatedBytes: 123, filePath: "/tmp/file-3.txt", workerId: 2));

        var id = RunFileProjector.CreateId("member a", "script-1", 3);
        var card = Assert.Single(result).Value;
        Assert.Equal(id, card.Id);
        Assert.Equal(RunScriptProjector.CreateId("Member A", "SCRIPT-1"), card.ParentScriptId);
        Assert.Equal(FileRunStage.Written, card.Stage);
        Assert.Equal(QueuedAt, card.CreatedAt);
        Assert.Equal(QueuedAt, card.QueuedAt);
        Assert.Equal(writtenAt, card.StageEnteredAt);
        Assert.Equal(writtenAt, card.CompletedAt);
        Assert.Equal("file-3.txt", card.FileName);
        Assert.Equal("/tmp/file-3.txt", card.FilePath);
        Assert.Equal(7, card.Rows);
        Assert.Equal(123, card.EstimatedBytes);
        Assert.Equal(2, card.WorkerId);
    }

    [Fact]
    public void Apply_IsCaseInsensitiveAndRepeatedStagePreservesStageEnteredAt()
    {
        var initial = RunFileProjector.Apply(
            source: null,
            Event(RunnerStep.FileWriteStarted, occurredAt: QueuedAt, memberName: "Member A", scriptCode: "SCRIPT-1", chunkNumber: 1));
        var repeatedAt = QueuedAt.AddMinutes(1);
        var repeated = RunFileProjector.Apply(
            initial,
            Event(RunnerStep.FileWriteStarted, occurredAt: repeatedAt, memberName: "member a", scriptCode: "script-1", chunkNumber: 1, workerId: 3));
        var writtenAt = QueuedAt.AddMinutes(2);
        var written = RunFileProjector.Apply(
            repeated,
            Event(RunnerStep.FileWritten, occurredAt: writtenAt, memberName: "MEMBER A", scriptCode: "SCRIPT-1", chunkNumber: 1, filePath: "/tmp/one.txt"));
        var duplicatedWritten = RunFileProjector.Apply(
            written,
            Event(RunnerStep.FileWritten, occurredAt: writtenAt.AddMinutes(1), memberName: "member a", scriptCode: "script-1", chunkNumber: 1, filePath: "/tmp/one.txt"));

        Assert.Equal(RunFileProjector.CreateId("Member A", "SCRIPT-1", 1), RunFileProjector.CreateId("member a", "script-1", 1));
        var queuedCard = Assert.Single(repeated).Value;
        Assert.Equal(QueuedAt, queuedCard.StageEnteredAt);
        Assert.Equal(repeatedAt, queuedCard.UpdatedAt);
        Assert.Equal(3, queuedCard.WorkerId);
        var writtenCard = Assert.Single(duplicatedWritten).Value;
        Assert.Equal(FileRunStage.Written, writtenCard.Stage);
        Assert.Equal(writtenAt, writtenCard.StageEnteredAt);
        Assert.Equal(writtenAt, writtenCard.CompletedAt);
    }

    [Theory]
    [InlineData(FileRunStage.Failed)]
    [InlineData(FileRunStage.Cancelled)]
    public void UpdateUnfinished_ChangesOnlyIncompleteCards(FileRunStage terminalStage)
    {
        var queued = RunFileProjector.Apply(source: null, Event(RunnerStep.FileWriteStarted));
        var written = RunFileProjector.Apply(
            queued,
            Event(RunnerStep.FileWritten, memberName: "Member B", scriptCode: "SCRIPT-2", chunkNumber: 1, filePath: "/tmp/two.txt"));
        var terminalAt = QueuedAt.AddMinutes(3);
        var result = terminalStage == FileRunStage.Failed
            ? RunFileProjector.FailUnfinished(written, "stopped", terminalAt)
            : RunFileProjector.CancelUnfinished(written, "stopped", terminalAt);

        Assert.Equal(terminalStage, result[RunFileProjector.CreateId("Member A", "SCRIPT-1", 1)].Stage);
        Assert.Equal(FileRunStage.Written, result[RunFileProjector.CreateId("Member B", "SCRIPT-2", 1)].Stage);
    }

    [Fact]
    public void Apply_LateEventDoesNotReopenTerminalCard()
    {
        var failed = RunFileProjector.FailUnfinished(
            RunFileProjector.Apply(source: null, Event(RunnerStep.FileWriteStarted)),
            "write failed",
            QueuedAt.AddSeconds(1));

        var result = RunFileProjector.Apply(
            failed,
            Event(RunnerStep.FileWritten, occurredAt: QueuedAt.AddSeconds(2), filePath: "/tmp/late.txt"));

        var card = Assert.Single(result).Value;
        Assert.Equal(FileRunStage.Failed, card.Stage);
        Assert.Null(card.FilePath);
    }

    private static RunnerEvent Event(
        RunnerStep step,
        DateTimeOffset? occurredAt = null,
        string memberName = "Member A",
        string scriptCode = "SCRIPT-1",
        int chunkNumber = 1,
        int? records = null,
        long? estimatedBytes = null,
        string? filePath = null,
        int? workerId = null)
    {
        return new RunnerEvent(
            occurredAt ?? QueuedAt,
            "run-1",
            step,
            step.ToString(),
            memberName,
            scriptCode,
            records,
            filePath,
            workerId,
            chunkNumber,
            estimatedBytes);
    }
}
