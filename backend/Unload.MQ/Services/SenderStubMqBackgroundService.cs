using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.MQ;

/// <summary>
/// Фоновая in-memory заглушка sender-а.
/// Обрабатывает batch-ready сообщения и публикует feedback c задержкой 1 секунда на файл.
/// </summary>
public sealed class SenderStubMqBackgroundService : BackgroundService
{
    private static readonly TimeSpan FileSendDelay = TimeSpan.FromSeconds(1);

    private readonly IMqFileBatchSource _batchSource;
    private readonly IMqPublisher _mqPublisher;
    private readonly ILogger<SenderStubMqBackgroundService> _logger;

    public SenderStubMqBackgroundService(
        IMqFileBatchSource batchSource,
        IMqPublisher mqPublisher,
        ILogger<SenderStubMqBackgroundService> logger)
    {
        _batchSource = batchSource;
        _mqPublisher = mqPublisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var batch in _batchSource.ReadBatchReadyEventsAsync(stoppingToken))
        {
            await ProcessBatchAsync(batch, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(SenderFileBatchReadyEvent batch, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Sender stub started batch. CorrelationId: {CorrelationId}, Member: {MemberName}, Files: {FilesCount}",
                batch.CorrelationId,
                batch.MemberName,
                batch.Files.Count);

            foreach (var file in batch.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(FileSendDelay, cancellationToken);

                await _mqPublisher.PublishSenderFeedbackAsync(
                    new SenderFileDispatchFeedback(
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: batch.CorrelationId,
                        MemberName: batch.MemberName,
                        BatchId: batch.BatchId,
                        Kind: SenderFeedbackKind.FileSent,
                        FilePath: file.FilePath,
                        Message: $"File sent: {file.FileName}"),
                    cancellationToken);
            }

            await _mqPublisher.PublishSenderFeedbackAsync(
                new SenderFileDispatchFeedback(
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: batch.CorrelationId,
                    MemberName: batch.MemberName,
                    BatchId: batch.BatchId,
                    Kind: SenderFeedbackKind.BatchCompleted,
                    Message: $"Batch completed. Files sent: {batch.Files.Count}."),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PublishSafeAsync(
                new SenderFileDispatchFeedback(
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: batch.CorrelationId,
                    MemberName: batch.MemberName,
                    BatchId: batch.BatchId,
                    Kind: SenderFeedbackKind.BatchFailed,
                    Message: "Sender was cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Sender stub failed batch. CorrelationId: {CorrelationId}, Member: {MemberName}",
                batch.CorrelationId,
                batch.MemberName);

            await PublishSafeAsync(
                new SenderFileDispatchFeedback(
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: batch.CorrelationId,
                    MemberName: batch.MemberName,
                    BatchId: batch.BatchId,
                    Kind: SenderFeedbackKind.BatchFailed,
                    Message: ex.Message));
        }
    }

    private async Task PublishSafeAsync(SenderFileDispatchFeedback feedback)
    {
        try
        {
            await _mqPublisher.PublishSenderFeedbackAsync(feedback, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish sender feedback. CorrelationId: {CorrelationId}, BatchId: {BatchId}, Kind: {Kind}",
                feedback.CorrelationId,
                feedback.BatchId,
                feedback.Kind);
        }
    }
}
