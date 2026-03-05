using System.Threading.Channels;
using Unload.Core;

namespace Unload.Application;

/// <summary>
/// In-memory реализация <see cref="IRunCoordinator"/> без очереди ожидания.
/// Гарантирует, что одновременно активен только один запуск.
/// </summary>
public class InMemoryRunCoordinator : IRunCoordinator
{
    private readonly Channel<RunActivation> _channel;
    private readonly object _sync = new();
    private ActiveRunContext? _active;

    /// <summary>
    /// Инициализирует координатор с bounded-каналом на один элемент.
    /// </summary>
    public InMemoryRunCoordinator()
    {
        _channel = Channel.CreateBounded<RunActivation>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Пытается занять слот активного запуска и передать запрос в канал обработки.
    /// </summary>
    /// <param name="request">Запрос запуска раннера.</param>
    /// <returns><c>true</c>, если запуск активирован; иначе <c>false</c>.</returns>
    public bool TryActivate(RunRequest request)
    {
        lock (_sync)
        {
            if (_active is not null)
            {
                return false;
            }

            var runCts = new CancellationTokenSource();
            var activation = new RunActivation(request, runCts.Token);
            _active = new ActiveRunContext(request.CorrelationId, runCts);
            if (_channel.Writer.TryWrite(activation))
            {
                return true;
            }

            _active = null;
            runCts.Dispose();
            return false;
        }
    }

    /// <summary>
    /// Возвращает поток активаций, потребляемых фоновым обработчиком.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены чтения.</param>
    /// <returns>Асинхронный поток запросов запуска.</returns>
    public IAsyncEnumerable<RunActivation> ReadActivationsAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Освобождает активный слот для указанного запуска.
    /// </summary>
    /// <param name="correlationId">Идентификатор завершенного запуска.</param>
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

    /// <summary>
    /// Возвращает идентификатор текущего активного запуска.
    /// </summary>
    /// <returns>Correlation id активного запуска или <c>null</c>.</returns>
    public string? GetActiveCorrelationId()
    {
        lock (_sync)
        {
            return _active?.CorrelationId;
        }
    }

    /// <summary>
    /// Отправляет запрос отмены активного запуска по correlation id.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <returns><c>true</c>, если запрос отмены отправлен; иначе <c>false</c>.</returns>
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

    private sealed record ActiveRunContext(string CorrelationId, CancellationTokenSource Cancellation);
}
