namespace Unload.Workflow;

/// <summary>
/// Активация элемента глобального workflow с токеном отмены.
/// </summary>
/// <typeparam name="TPayload">Тип полезной нагрузки задачи workflow.</typeparam>
/// <param name="CorrelationId">Идентификатор активации.</param>
/// <param name="Payload">Полезная нагрузка активации.</param>
/// <param name="CancellationToken">Токен отмены конкретной активации.</param>
public  record WorkflowActivation<TPayload>(
    string CorrelationId,
    TPayload Payload,
    CancellationToken CancellationToken);

/// <summary>
/// Контракт single-active workflow пайплайна.
/// В каждый момент времени активна только одна задача.
/// </summary>
/// <typeparam name="TPayload">Тип полезной нагрузки задачи workflow.</typeparam>
public interface ISingleActiveWorkflow<TPayload>
{
    /// <summary>
    /// Пытается активировать новую задачу.
    /// </summary>
    /// <param name="correlationId">Идентификатор задачи.</param>
    /// <param name="payload">Полезная нагрузка.</param>
    /// <returns><c>true</c>, если активация принята; иначе <c>false</c>.</returns>
    bool TryActivate(string correlationId, TPayload payload);

    /// <summary>
    /// Возвращает асинхронный поток принятых активаций.
    /// </summary>
    /// <param name="cancellationToken">Токен остановки чтения активаций.</param>
    /// <returns>Поток активаций для фонового обработчика.</returns>
    IAsyncEnumerable<WorkflowActivation<TPayload>> ReadActivationsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Освобождает слот активной задачи.
    /// </summary>
    /// <param name="correlationId">Идентификатор завершенной задачи.</param>
    void Complete(string correlationId);

    /// <summary>
    /// Возвращает идентификатор активной задачи.
    /// </summary>
    /// <returns>Идентификатор активной задачи или <c>null</c>.</returns>
    string? GetActiveCorrelationId();

    /// <summary>
    /// Пытается запросить отмену активной задачи.
    /// </summary>
    /// <param name="correlationId">Идентификатор задачи для отмены.</param>
    /// <returns><c>true</c>, если отмена запрошена; иначе <c>false</c>.</returns>
    bool TryCancel(string correlationId);
}
