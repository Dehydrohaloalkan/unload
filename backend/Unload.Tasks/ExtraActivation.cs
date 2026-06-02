namespace Unload.Tasks;

/// <summary>
/// Активация extra-задачи с токеном отмены. Аналог <see cref="RunActivation"/>.
/// </summary>
/// <param name="CorrelationId">Идентификатор активации.</param>
/// <param name="Payload">Запрос extra-выгрузки.</param>
/// <param name="CancellationToken">Токен отмены конкретной активации.</param>
public record ExtraActivation(
    string CorrelationId,
    ExtraRunRequest Payload,
    CancellationToken CancellationToken);
