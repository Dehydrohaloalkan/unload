using Unload.Core;

namespace Unload.Application;

/// <summary>
/// Фабрика формирования стандартного запроса запуска выгрузки.
/// Используется orchestrator для генерации уникального correlation id.
/// </summary>
public class RunRequestFactory : IRunRequestFactory
{
    /// <summary>
    /// Создает <see cref="RunRequest"/> c уникальным идентификатором запуска.
    /// </summary>
    /// <param name="profileCodes">Коды профилей для выполнения.</param>
    /// <param name="outputDirectory">Базовая директория вывода.</param>
    /// <returns>Новый объект запроса запуска.</returns>
    public RunRequest Create(IReadOnlyCollection<string> profileCodes, string outputDirectory)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return new RunRequest(
            profileCodes,
            CorrelationId: $"req-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{suffix}",
            OutputDirectory: outputDirectory);
    }
}
