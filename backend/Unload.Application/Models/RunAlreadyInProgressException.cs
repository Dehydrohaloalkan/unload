namespace Unload.Application;

/// <summary>
/// Ошибка попытки запуска новой выгрузки при уже активном запуске.
/// </summary>
public class RunAlreadyInProgressException : InvalidOperationException
{
    /// <summary>
    /// Создает исключение с идентификатором активного запуска.
    /// </summary>
    /// <param name="activeCorrelationId">Идентификатор уже выполняющегося запуска.</param>
    public RunAlreadyInProgressException(string? activeCorrelationId)
        : base(activeCorrelationId is null
            ? "Another run is already in progress."
            : $"Run '{activeCorrelationId}' is already in progress.")
    {
        ActiveCorrelationId = activeCorrelationId;
    }

    /// <summary>
    /// Идентификатор уже активного запуска, если известен.
    /// </summary>
    public string? ActiveCorrelationId { get; }
}
