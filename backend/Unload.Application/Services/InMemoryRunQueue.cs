using System.Threading.Channels;
using Unload.Core;

namespace Unload.Application;

public class InMemoryRunQueue : IRunQueue
{
    private readonly Channel<RunRequest> _channel;

    public InMemoryRunQueue()
    {
        _channel = Channel.CreateBounded<RunRequest>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryEnqueue(RunRequest request)
    {
        return _channel.Writer.TryWrite(request);
    }

    public IAsyncEnumerable<RunRequest> DequeueAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
