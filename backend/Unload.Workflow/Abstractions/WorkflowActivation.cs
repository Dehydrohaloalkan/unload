namespace Unload.Workflow;

/// <summary>
/// Активация элемента глобального workflow с токеном отмены.
/// </summary>
/// <typeparam name="TPayload">Тип полезной нагрузки задачи workflow.</typeparam>
/// <param name="CorrelationId">Идентификатор активации.</param>
/// <param name="Payload">Полезная нагрузка активации.</param>
/// <param name="CancellationToken">Токен отмены конкретной активации.</param>
public record WorkflowActivation<TPayload>(
    string CorrelationId,
    TPayload Payload,
    CancellationToken CancellationToken);

