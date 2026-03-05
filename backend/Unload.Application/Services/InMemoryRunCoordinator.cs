using System.Threading.Channels;
using Unload.Core;

namespace Unload.Application;

/// <summary>
/// In-memory реализация <see cref="IRunCoordinator"/> без очереди ожидания.
/// Гарантирует, что одновременно активен только один запуск.
/// </summary>
public class InMemoryRunCoordinator : IRunCoordinator
{
    private readonly Channel<RunRequest> _channel;
    private readonly object _sync = new();
    private string? _activeCorrelationId;

    /// <summary>
    /// Инициализирует координатор с bounded-каналом на один элемент.
    /// </summary>
    public InMemoryRunCoordinator()
    {
        _channel = Channel.CreateBounded<RunRequest>(new BoundedChannelOptions(1)
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

    /// <summary>
    /// Возвращает поток активаций, потребляемых фоновым обработчиком.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены чтения.</param>
    /// <returns>Асинхронный поток запросов запуска.</returns>
    public IAsyncEnumerable<RunRequest> ReadActivationsAsync(CancellationToken cancellationToken)
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
            if (string.Equals(_activeCorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
            {
                _activeCorrelationId = null;
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
            return _activeCorrelationId;
        }
    }
}
