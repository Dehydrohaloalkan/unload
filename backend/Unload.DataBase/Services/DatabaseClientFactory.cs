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
/// <param name="credentialStore">Runtime-хранилище пароля и шаблона подключения.</param>
public class DatabaseClientFactory(
    int timeoutSeconds,
    IDatabaseCredentialStore credentialStore) : IDatabaseClientFactory
{
    private readonly int _timeoutSeconds = timeoutSeconds;
    private readonly IDatabaseCredentialStore _credentialStore = credentialStore;

    /// <inheritdoc />
    public IDatabaseClient CreateClient()
    {
        return new StubDatabaseClient(_timeoutSeconds, _credentialStore.BuildConnectionString());
    }
}
