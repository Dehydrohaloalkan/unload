using Unload.Core;

namespace Unload.Tasks;

/// <summary>
/// Активация основной задачи выгрузки с токеном отмены.
/// Не-generic замена <c>WorkflowActivation&lt;RunRequest&gt;</c>.
/// </summary>
/// <param name="CorrelationId">Идентификатор активации.</param>
/// <param name="Payload">Запрос выгрузки.</param>
/// <param name="CancellationToken">Токен отмены конкретной активации.</param>
public record RunActivation(
    string CorrelationId,
    RunRequest Payload,
    CancellationToken CancellationToken);
