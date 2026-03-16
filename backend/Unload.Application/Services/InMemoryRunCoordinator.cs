using Unload.Core;
using Unload.Workflow;

namespace Unload.Application;

/// <summary>
/// In-memory реализация <see cref="IRunCoordinator"/> без очереди ожидания.
/// Гарантирует, что одновременно активен только один запуск.
/// </summary>
public class InMemoryRunCoordinator : IRunCoordinator
{
    private readonly ISingleActiveWorkflow<RunRequest> _workflow;

    public InMemoryRunCoordinator(ISingleActiveWorkflow<RunRequest> workflow)
    {
        _workflow = workflow;
    }

    /// <summary>
    /// Пытается занять слот активного запуска и передать запрос в канал обработки.
    /// </summary>
    /// <param name="request">Запрос запуска раннера.</param>
    /// <returns><c>true</c>, если запуск активирован; иначе <c>false</c>.</returns>
    public bool TryActivate(RunRequest request)
    {
        return _workflow.TryActivate(request.CorrelationId, request);
    }

    /// <summary>
    /// Возвращает поток активаций, потребляемых фоновым обработчиком.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены чтения.</param>
    /// <returns>Асинхронный поток запросов запуска.</returns>
    public async IAsyncEnumerable<RunActivation> ReadActivationsAsync(CancellationToken cancellationToken)
    {
        await foreach (var activation in _workflow.ReadActivationsAsync(cancellationToken))
        {
            yield return new RunActivation(activation.Payload, activation.CancellationToken);
        }
    }

    /// <summary>
    /// Освобождает активный слот для указанного запуска.
    /// </summary>
    /// <param name="correlationId">Идентификатор завершенного запуска.</param>
    public void Complete(string correlationId)
    {
        _workflow.Complete(correlationId);
    }

    /// <summary>
    /// Возвращает идентификатор текущего активного запуска.
    /// </summary>
    /// <returns>Correlation id активного запуска или <c>null</c>.</returns>
    public string? GetActiveCorrelationId()
    {
        return _workflow.GetActiveCorrelationId();
    }

    /// <summary>
    /// Отправляет запрос отмены активного запуска по correlation id.
    /// </summary>
    /// <param name="correlationId">Идентификатор запуска.</param>
    /// <returns><c>true</c>, если запрос отмены отправлен; иначе <c>false</c>.</returns>
    public bool TryCancel(string correlationId)
    {
        return _workflow.TryCancel(correlationId);
    }
}
