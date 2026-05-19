using System.Threading.Channels;
using Unload.Core;

namespace Unload.Gateway;

/// <summary>
/// Транспортный слой шлюза отправки файлов.
/// Принимает batch-ready события и feedback через in-process каналы;
/// фактическая FTP-отправка выполняется в <see cref="FtpGatewayBackgroundService"/>.
/// </summary>
public class FtpGatewayPublisher : IGatewayPublisher, IGatewayBatchSource, IGatewaySenderFeedbackSource
{
    private readonly Channel<SenderFileBatchReadyEvent> _batchReadyChannel =
        Channel.CreateUnbounded<SenderFileBatchReadyEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    private readonly Channel<SenderFileDispatchFeedback> _senderFeedbackChannel =
        Channel.CreateUnbounded<SenderFileDispatchFeedback>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    public Task PublishFileBatchReadyAsync(SenderFileBatchReadyEvent @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _batchReadyChannel.Writer.TryWrite(@event);
        return Task.CompletedTask;
    }

    public Task PublishSenderFeedbackAsync(SenderFileDispatchFeedback feedback, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _senderFeedbackChannel.Writer.TryWrite(feedback);
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<SenderFileBatchReadyEvent> ReadBatchReadyEventsAsync(CancellationToken cancellationToken)
        => _batchReadyChannel.Reader.ReadAllAsync(cancellationToken);

    public IAsyncEnumerable<SenderFileDispatchFeedback> ReadSenderFeedbackAsync(CancellationToken cancellationToken)
        => _senderFeedbackChannel.Reader.ReadAllAsync(cancellationToken);
}
