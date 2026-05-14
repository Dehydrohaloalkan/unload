namespace Unload.Core;

public interface IGatewayBatchSource
{
    IAsyncEnumerable<SenderFileBatchReadyEvent> ReadBatchReadyEventsAsync(CancellationToken cancellationToken);
}
