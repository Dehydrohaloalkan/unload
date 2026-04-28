using Unload.Core;

namespace Unload.DataBase;

/// <summary>
/// Фабрика клиентов БД для runtime.
/// Создает новый экземпляр клиента на каждый запрос фабрики.
/// </summary>
/// <remarks>
/// Создает фабрику с общими настройками подключения.
/// </remarks>
/// <param name="timeoutSeconds">Таймаут выполнения запросов в секундах.</param>
/// <param name="connectionString">Строка подключения в plain или dpapi-формате.</param>
public class DatabaseClientFactory(int timeoutSeconds, string connectionString) : IDatabaseClientFactory
{
    private readonly int _timeoutSeconds = timeoutSeconds;
    private readonly string _connectionString = connectionString;

    /// <inheritdoc />
    public IDatabaseClient CreateClient()
    {
        return new StubDatabaseClient(_timeoutSeconds, _connectionString);
    }
}
