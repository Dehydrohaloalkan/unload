using System.Threading.Channels;

namespace Unload.Workflow;

/// <summary>
/// In-memory реализация single-active workflow без очереди ожидания.
/// Гарантирует, что одновременно активна только одна задача.
/// </summary>
/// <typeparam name="TPayload">Тип полезной нагрузки задачи workflow.</typeparam>
public class InMemorySingleActiveWorkflow<TPayload> : ISingleActiveWorkflow<TPayload>
{
    private readonly Channel<WorkflowActivation<TPayload>> _channel;
    private readonly object _sync = new();
    private ActiveWorkflowContext? _active;

    public InMemorySingleActiveWorkflow()
    {
        _channel = Channel.CreateBounded<WorkflowActivation<TPayload>>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryActivate(string correlationId, TPayload payload)
    {
        lock (_sync)
        {
            if (_active is not null)
            {
                return false;
            }

            var activationCts = new CancellationTokenSource();
            var activation = new WorkflowActivation<TPayload>(correlationId, payload, activationCts.Token);
            _active = new ActiveWorkflowContext(correlationId, activationCts);
            if (_channel.Writer.TryWrite(activation))
            {
                return true;
            }

            _active = null;
            activationCts.Dispose();
            return false;
        }
    }

    public IAsyncEnumerable<WorkflowActivation<TPayload>> ReadActivationsAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public void Complete(string correlationId)
    {
        lock (_sync)
        {
            if (_active is not null &&
                string.Equals(_active.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
            {
                _active.Cancellation.Dispose();
                _active = null;
            }
        }
    }

    public string? GetActiveCorrelationId()
    {
        lock (_sync)
        {
            return _active?.CorrelationId;
        }
    }

    public bool TryCancel(string correlationId)
    {
        lock (_sync)
        {
            if (_active is null ||
                !string.Equals(_active.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_active.Cancellation.IsCancellationRequested)
            {
                return true;
            }

            _active.Cancellation.Cancel();
            return true;
        }
    }

    private  record ActiveWorkflowContext(string CorrelationId, CancellationTokenSource Cancellation);
}
