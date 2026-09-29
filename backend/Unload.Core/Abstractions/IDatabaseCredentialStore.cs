namespace Unload.Core;

/// <summary>
/// Хранит введённый пользователем пароль БД только в памяти процесса.
/// </summary>
public interface IDatabaseCredentialStore
{
    bool IsConfigured { get; }

    string? SelectedDatabaseId { get; }

    IReadOnlyList<DatabaseOption> Databases { get; }

    void SetCredentials(string databaseId, string username, string password);

    void Clear();

    string BuildConnectionString();
}

public sealed record DatabaseOption(string Id, string Name);
