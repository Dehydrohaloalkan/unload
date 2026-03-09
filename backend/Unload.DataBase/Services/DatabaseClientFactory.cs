using Unload.Core;

namespace Unload.DataBase;

/// <summary>
/// Фабрика клиентов БД для runtime.
/// Создает новый экземпляр клиента на каждый запрос фабрики.
/// </summary>
public sealed class DatabaseClientFactory : IDatabaseClientFactory
{
    private readonly int _timeoutSeconds;
    private readonly string _connectionString;

    /// <summary>
    /// Создает фабрику с общими настройками подключения.
    /// </summary>
    /// <param name="timeoutSeconds">Таймаут выполнения запросов в секундах.</param>
    /// <param name="connectionString">Строка подключения в plain или dpapi-формате.</param>
    public DatabaseClientFactory(int timeoutSeconds, string connectionString)
    {
        _timeoutSeconds = timeoutSeconds;
        _connectionString = connectionString;
    }

    /// <inheritdoc />
    public IDatabaseClient CreateClient()
    {
        return new StubDatabaseClient(_timeoutSeconds, _connectionString);
    }
}
