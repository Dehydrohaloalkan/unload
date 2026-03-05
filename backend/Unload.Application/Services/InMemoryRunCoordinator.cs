using System.Threading.Channels;
using Unload.Core;

namespace Unload.Application;

public class InMemoryRunCoordinator : IRunCoordinator
{
    private readonly Channel<RunRequest> _channel;
    private readonly object _sync = new();
    private string? _activeCorrelationId;

    public InMemoryRunCoordinator()
    {
        _channel = Channel.CreateBounded<RunRequest>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryActivate(RunRequest request)
    {
        lock (_sync)
        {
            if (_activeCorrelationId is not null)
            {
                return false;
            }

            _activeCorrelationId = request.CorrelationId;
            if (_channel.Writer.TryWrite(request))
            {
                return true;
            }

            _activeCorrelationId = null;
            return false;
        }
    }

    public IAsyncEnumerable<RunRequest> ReadActivationsAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public void Complete(string correlationId)
    {
        lock (_sync)
        {
            if (string.Equals(_activeCorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
            {
                _activeCorrelationId = null;
            }
        }
    }

    public string? GetActiveCorrelationId()
    {
        lock (_sync)
        {
            return _activeCorrelationId;
        }
    }
}
