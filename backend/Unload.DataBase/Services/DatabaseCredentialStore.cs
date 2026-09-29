using System.Data.Common;
using Unload.Core;

namespace Unload.DataBase;

/// <summary>
/// Добавляет runtime-пароль к несекретной строке подключения.
/// Пароль не сохраняется на диск и теряется при остановке процесса.
/// </summary>
public sealed class DatabaseCredentialStore(
    IReadOnlyCollection<DatabaseConnectionDefinition> definitions) : IDatabaseCredentialStore
{
    private readonly IReadOnlyDictionary<string, DatabaseConnectionDefinition> _definitions =
        CreateDefinitions(definitions);
    private DatabaseCredentials? _credentials;

    public bool IsConfigured => Volatile.Read(ref _credentials) is not null;

    public string? SelectedDatabaseId => Volatile.Read(ref _credentials)?.DatabaseId;

    public IReadOnlyList<DatabaseOption> Databases { get; } = definitions
        .Select(static definition => new DatabaseOption(definition.Id, definition.Name))
        .ToArray();

    public void SetCredentials(string databaseId, string username, string password)
    {
        if (!_definitions.ContainsKey(databaseId))
        {
            throw new ArgumentException("Selected database is not configured.", nameof(databaseId));
        }
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Database username must not be empty.", nameof(username));
        }
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Database password must not be empty.", nameof(password));
        }

        Volatile.Write(ref _credentials, new DatabaseCredentials(databaseId, username.Trim(), password));
    }

    public void Clear()
    {
        Volatile.Write(ref _credentials, null);
    }

    public string BuildConnectionString()
    {
        var credentials = Volatile.Read(ref _credentials)
            ?? throw new InvalidOperationException("Database credentials are required.");
        var definition = _definitions[credentials.DatabaseId];
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = definition.ConnectionString,
        };
        RemoveCredentialKeys(builder);
        builder["User ID"] = credentials.Username;
        builder["Password"] = credentials.Password;
        return builder.ConnectionString;
    }

    private static IReadOnlyDictionary<string, DatabaseConnectionDefinition> CreateDefinitions(
        IReadOnlyCollection<DatabaseConnectionDefinition> definitions)
    {
        if (definitions.Count == 0)
        {
            throw new ArgumentException("At least one database must be configured.", nameof(definitions));
        }

        return definitions.ToDictionary(static definition => definition.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static void RemoveCredentialKeys(DbConnectionStringBuilder builder)
    {
        foreach (var key in new[] { "User ID", "UserID", "UID", "Password", "PWD" })
        {
            builder.Remove(key);
        }
    }

    private sealed record DatabaseCredentials(string DatabaseId, string Username, string Password);
}

public sealed record DatabaseConnectionDefinition(string Id, string Name, string ConnectionString);
