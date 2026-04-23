using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.MQ;

/// <summary>
/// Фоновая in-memory заглушка sender-а.
/// Обрабатывает batch-ready сообщения и публикует feedback c задержкой 1 секунда на файл.
/// </summary>
public  class SenderStubMqBackgroundService : BackgroundService
{
    private static readonly TimeSpan FileSendDelay = TimeSpan.FromSeconds(1);

    private readonly IMqFileBatchSource _batchSource;
    private readonly IMqPublisher _mqPublisher;
    private readonly ILogger<SenderStubMqBackgroundService> _logger;
    private readonly ConcurrentDictionary<string, byte> _failedMembers = new(StringComparer.OrdinalIgnoreCase);

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
        var memberName = (batch.MemberName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(memberName))
        {
            return;
        }

        if (_failedMembers.ContainsKey(memberName))
        {
            await PublishSafeAsync(
                new SenderFileDispatchFeedback(
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: batch.CorrelationId,
                    MemberName: memberName,
                    BatchId: batch.BatchId,
                    Kind: SenderFeedbackKind.BatchFailed,
                    Message: "Member dispatch is blocked due to a previous send failure."));
            return;
        }

        try
        {
            _logger.LogInformation(
                "Sender stub started batch. CorrelationId: {CorrelationId}, Member: {MemberName}, Files: {FilesCount}",
                batch.CorrelationId,
                memberName,
                batch.Files.Count);

            foreach (var file in batch.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(FileSendDelay, cancellationToken);

                try
                {
                    await _mqPublisher.PublishSenderFeedbackAsync(
                        new SenderFileDispatchFeedback(
                            OccurredAt: DateTimeOffset.UtcNow,
                            CorrelationId: batch.CorrelationId,
                            MemberName: memberName,
                            BatchId: batch.BatchId,
                            Kind: SenderFeedbackKind.FileSent,
                            FilePath: file.FilePath,
                            Message: $"File sent: {file.FileName}"),
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _failedMembers.TryAdd(memberName, 0);
                    _logger.LogError(
                        ex,
                        "Sender stub failed to dispatch file. CorrelationId: {CorrelationId}, Member: {MemberName}, File: {FilePath}",
                        batch.CorrelationId,
                        memberName,
                        file.FilePath);

                    await PublishSafeAsync(
                        new SenderFileDispatchFeedback(
                            OccurredAt: DateTimeOffset.UtcNow,
                            CorrelationId: batch.CorrelationId,
                            MemberName: memberName,
                            BatchId: batch.BatchId,
                            Kind: SenderFeedbackKind.BatchFailed,
                            FilePath: file.FilePath,
                            Message: ex.Message));
                    return;
                }
            }

            try
            {
                await _mqPublisher.PublishSenderFeedbackAsync(
                    new SenderFileDispatchFeedback(
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: batch.CorrelationId,
                        MemberName: memberName,
                        BatchId: batch.BatchId,
                        Kind: SenderFeedbackKind.BatchCompleted,
                        Message: $"Batch completed. Files sent: {batch.Files.Count}."),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _failedMembers.TryAdd(memberName, 0);
                _logger.LogError(
                    ex,
                    "Sender stub failed to complete batch. CorrelationId: {CorrelationId}, Member: {MemberName}",
                    batch.CorrelationId,
                    memberName);

                await PublishSafeAsync(
                    new SenderFileDispatchFeedback(
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: batch.CorrelationId,
                        MemberName: memberName,
                        BatchId: batch.BatchId,
                        Kind: SenderFeedbackKind.BatchFailed,
                        Message: ex.Message));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PublishSafeAsync(
                new SenderFileDispatchFeedback(
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: batch.CorrelationId,
                    MemberName: memberName,
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
                memberName);

            _failedMembers.TryAdd(memberName, 0);
            await PublishSafeAsync(
                new SenderFileDispatchFeedback(
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: batch.CorrelationId,
                    MemberName: memberName,
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
