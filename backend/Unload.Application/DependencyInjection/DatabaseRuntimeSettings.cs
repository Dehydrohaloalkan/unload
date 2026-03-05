namespace Unload.Application;

/// <summary>
/// Настройки клиента БД, читаемые из appsettings.
/// </summary>
public sealed class DatabaseRuntimeSettings
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
    /// Строка подключения (plain или формат dpapi:&lt;base64&gt;).
    /// </summary>
    public string ConnectionString { get; init; } = "Server=localhost;Database=unload;User Id=stub;Password=stub;";
}
