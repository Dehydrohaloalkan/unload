using Unload.Core;

namespace Unload.Tasks.MainUnload;

/// <summary>
/// Фабрика формирования стандартного запроса запуска выгрузки.
/// Генерирует уникальный correlation id для каждого запуска.
/// </summary>
public class RunRequestFactory
{
    /// <summary>
    /// Создает <see cref="RunRequest"/> c уникальным идентификатором запуска.
    /// </summary>
    public RunRequest Create(IReadOnlyCollection<string> targetCodes, string outputDirectory, bool publishToGateway = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return new RunRequest(
            targetCodes,
            CorrelationId: $"req-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{suffix}",
            OutputDirectory: outputDirectory,
            PublishToGateway: publishToGateway);
    }
}
