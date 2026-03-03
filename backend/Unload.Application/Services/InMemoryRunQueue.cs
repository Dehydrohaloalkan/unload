using System.Threading.Channels;
using Unload.Core;

namespace Unload.Application;

/// <summary>
/// In-memory реализация очереди запусков на базе <see cref="Channel{T}"/>.
/// Используется background worker для последовательного чтения запросов выполнения.
/// </summary>
public class InMemoryRunQueue : IRunQueue
{
    private readonly Channel<RunRequest> _channel;

    /// <summary>
    /// Инициализирует ограниченную очередь запусков с единичным читателем.
    /// </summary>
    public InMemoryRunQueue()
    {
        _channel = Channel.CreateBounded<RunRequest>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Пытается добавить запрос в очередь выполнения.
    /// </summary>
    /// <param name="request">Запрос запуска.</param>
    /// <returns><c>true</c>, если запрос принят очередью; иначе <c>false</c>.</returns>
    public bool TryEnqueue(RunRequest request)
    {
        return _channel.Writer.TryWrite(request);
    }

    /// <summary>
    /// Возвращает поток всех запросов, поступающих в очередь.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены чтения.</param>
    /// <returns>Асинхронный поток запросов.</returns>
    public IAsyncEnumerable<RunRequest> DequeueAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
