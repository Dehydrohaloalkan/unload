namespace Unload.Core;

public interface IMqFileBatchSource
{
    IAsyncEnumerable<SenderFileBatchReadyEvent> ReadBatchReadyEventsAsync(CancellationToken cancellationToken);
}
