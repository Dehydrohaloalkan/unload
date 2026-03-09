namespace Unload.Core;

/// <summary>
/// Фабрика клиентов БД.
/// Используется раннером для получения независимых клиентов при параллельном выполнении SQL-скриптов.
/// </summary>
public interface IDatabaseClientFactory
{
    /// <summary>
    /// Создает новый клиент БД для выполнения запросов.
    /// </summary>
    /// <returns>Новый экземпляр клиента БД.</returns>
    IDatabaseClient CreateClient();
}
