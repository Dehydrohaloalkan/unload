using System.Collections.Concurrent;
using System.Threading.Channels;
using Unload.Core;

namespace Unload.MQ;

/// <summary>
/// In-memory заглушка публикатора MQ-событий.
/// Используется для локальной разработки без внешнего брокера сообщений.
/// </summary>
public class InMemoryMqPublisher : IMqPublisher, IMqFileBatchSource, IMqSenderFeedbackSource
{
    private readonly ConcurrentQueue<SenderFileBatchReadyEvent> _batchReadyEvents = new();
    private readonly ConcurrentQueue<SenderFileDispatchFeedback> _senderFeedbackEvents = new();
    private readonly Channel<SenderFileBatchReadyEvent> _batchReadyChannel = Channel.CreateUnbounded<SenderFileBatchReadyEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    private readonly Channel<SenderFileDispatchFeedback> _senderFeedbackChannel = Channel.CreateUnbounded<SenderFileDispatchFeedback>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    /// <summary>
    /// Сохраняет batch-ready событие в локальную очередь в памяти.
    /// </summary>
    /// <param name="event">Событие готовности набора файлов для sender.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Завершенная задача после помещения события в очередь.</returns>
    public Task PublishFileBatchReadyAsync(SenderFileBatchReadyEvent @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _batchReadyEvents.Enqueue(@event);
        _batchReadyChannel.Writer.TryWrite(@event);
        return Task.CompletedTask;
    }

    public Task PublishSenderFeedbackAsync(SenderFileDispatchFeedback feedback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _senderFeedbackEvents.Enqueue(feedback);
        _senderFeedbackChannel.Writer.TryWrite(feedback);
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<SenderFileBatchReadyEvent> ReadBatchReadyEventsAsync(CancellationToken cancellationToken)
    {
        return _batchReadyChannel.Reader.ReadAllAsync(cancellationToken);
    }

    public IAsyncEnumerable<SenderFileDispatchFeedback> ReadSenderFeedbackAsync(CancellationToken cancellationToken)
    {
        return _senderFeedbackChannel.Reader.ReadAllAsync(cancellationToken);
    }
}
