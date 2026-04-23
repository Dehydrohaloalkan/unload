using Unload.Core;

namespace Unload.Run.Application;

/// <summary>
/// Активация запуска с токеном отмены конкретного run.
/// </summary>
/// <param name="Request">Запрос на выполнение.</param>
/// <param name="CancellationToken">Токен остановки конкретного запуска.</param>
public record RunActivation(RunRequest Request, CancellationToken CancellationToken);

