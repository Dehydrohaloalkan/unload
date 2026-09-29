namespace Unload.Bootstrapper;

/// <summary>
/// Настройки клиента БД, читаемые из appsettings.
/// </summary>
public class DatabaseRuntimeSettings
{
    /// <summary>
    /// Имя секции в appsettings.
    /// </summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Таймаут выполнения запросов в секундах.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Доступные базы данных для выбора в интерфейсе.
    /// </summary>
    public List<DatabaseOptionSettings> Databases { get; init; } = [];
}

public sealed class DatabaseOptionSettings
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Несекретный шаблон строки подключения без пользователя и пароля.</summary>
    public string ConnectionString { get; init; } = string.Empty;
}
