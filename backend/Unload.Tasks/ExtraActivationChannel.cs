using System.Threading.Channels;

namespace Unload.Tasks;

/// <summary>
/// In-memory канал для single-active extra-выгрузки.
/// Гарантирует, что одновременно активна только одна extra-задача, и даёт точку отмены.
/// Аналог <see cref="RunActivationChannel"/> для main-выгрузки.
/// </summary>
public class ExtraActivationChannel
{
    private readonly Channel<ExtraActivation> _channel;
    private readonly object _sync = new();
    private ActiveContext? _active;

    public ExtraActivationChannel()
    {
        _channel = Channel.CreateBounded<ExtraActivation>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Пытается активировать новую extra-задачу.</summary>
    public bool TryActivate(string correlationId, ExtraRunRequest payload)
    {
        lock (_sync)
        {
            if (_active is not null)
            {
                return false;
            }

            var activationCts = new CancellationTokenSource();
            var activation = new ExtraActivation(correlationId, payload, activationCts.Token);
            _active = new ActiveContext(correlationId, activationCts);
            if (_channel.Writer.TryWrite(activation))
            {
                return true;
            }

            _active = null;
            activationCts.Dispose();
            return false;
        }
    }

    /// <summary>Возвращает асинхронный поток принятых активаций.</summary>
    public IAsyncEnumerable<ExtraActivation> ReadActivationsAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>Освобождает слот активной задачи.</summary>
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

    /// <summary>Возвращает идентификатор активной extra-задачи.</summary>
    public string? GetActiveCorrelationId()
    {
        lock (_sync)
        {
            return _active?.CorrelationId;
        }
    }

    /// <summary>Пытается запросить отмену активной extra-задачи.</summary>
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

    private record ActiveContext(string CorrelationId, CancellationTokenSource Cancellation);
}
