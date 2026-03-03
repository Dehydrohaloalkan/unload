namespace Unload.Core;

/// <summary>
/// Описывает входной запрос на запуск выгрузки.
/// Используется оркестратором и раннером для передачи профилей, идентификатора корреляции и целевой директории результатов.
/// </summary>
/// <param name="ProfileCodes">Нормализованный список кодов профилей для обработки.</param>
/// <param name="CorrelationId">Уникальный идентификатор запуска, используемый для трекинга статуса и событий.</param>
/// <param name="OutputDirectory">Базовая директория, внутри которой раннер создает папку текущего запуска.</param>
public record RunRequest(
    IReadOnlyCollection<string> ProfileCodes,
    string CorrelationId,
    string OutputDirectory);
