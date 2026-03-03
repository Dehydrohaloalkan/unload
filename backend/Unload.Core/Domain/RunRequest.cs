namespace Unload.Core;

/// <summary>
/// Описывает входной запрос на запуск выгрузки.
/// Используется оркестратором и раннером для передачи target-кодов, идентификатора корреляции и целевой директории результатов.
/// </summary>
/// <param name="TargetCodes">Нормализованный список target-кодов для обработки.</param>
/// <param name="CorrelationId">Уникальный идентификатор запуска, используемый для трекинга статуса и событий.</param>
/// <param name="OutputDirectory">Базовая директория, внутри которой раннер создает папку текущего запуска.</param>
public record RunRequest(
    IReadOnlyCollection<string> TargetCodes,
    string CorrelationId,
    string OutputDirectory);
