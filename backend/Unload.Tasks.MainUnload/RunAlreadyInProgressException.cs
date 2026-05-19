namespace Unload.Tasks.MainUnload;

/// <summary>
/// Ошибка попытки запуска новой выгрузки при уже активном запуске.
/// </summary>
/// <param name="activeCorrelationId">Идентификатор уже выполняющегося запуска.</param>
public class RunAlreadyInProgressException(string? activeCorrelationId) : InvalidOperationException(activeCorrelationId is null
            ? "Another run is already in progress."
            : $"Run '{activeCorrelationId}' is already in progress.")
{
    /// <summary>
    /// Идентификатор уже активного запуска, если известен.
    /// </summary>
    public string? ActiveCorrelationId { get; } = activeCorrelationId;
}
