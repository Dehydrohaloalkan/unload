namespace Unload.Tasks;

/// <summary>
/// Контракт single-active workflow пайплайна.
/// В каждый момент времени активна только одна задача.
/// Де-дженерикизируется в <c>RunActivationChannel</c> в Фазе 3.
/// </summary>
/// <typeparam name="TPayload">Тип полезной нагрузки задачи workflow.</typeparam>
public interface ISingleActiveWorkflow<TPayload>
{
    /// <summary>
    /// Пытается активировать новую задачу.
    /// </summary>
    bool TryActivate(string correlationId, TPayload payload);

    /// <summary>
    /// Возвращает асинхронный поток принятых активаций.
    /// </summary>
    IAsyncEnumerable<WorkflowActivation<TPayload>> ReadActivationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Освобождает слот активной задачи.
    /// </summary>
    void Complete(string correlationId);

    /// <summary>
    /// Возвращает идентификатор активной задачи.
    /// </summary>
    string? GetActiveCorrelationId();

    /// <summary>
    /// Пытается запросить отмену активной задачи.
    /// </summary>
    bool TryCancel(string correlationId);
}
