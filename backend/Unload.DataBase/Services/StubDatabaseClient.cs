using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Unload.Core;

namespace Unload.DataBase;

/// <summary>
/// Заглушка клиента БД, генерирующая тестовый <see cref="DbDataReader"/>.
/// Используется в development/demo режиме вместо реального подключения к базе данных.
/// </summary>
public class StubDatabaseClient : IDatabaseClient
{
    private readonly int _timeoutSeconds;
    private readonly string _connectionString;

    /// <summary>
    /// Создает заглушку клиента БД с настройками таймаута и строки подключения.
    /// Поддерживает plain строку и формат шифрования <c>dpapi:&lt;base64&gt;</c>.
    /// </summary>
    /// <param name="timeout">Таймаут в секундах.</param>
    /// <param name="connectionString">Строка подключения в plain или зашифрованном виде.</param>
    public StubDatabaseClient(int timeout, string connectionString)
    {
        if (timeout <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));
        }

        _timeoutSeconds = timeout;
        _connectionString = ResolveConnectionString(connectionString);
    }

    /// <summary>
    /// Всегда сообщает о доступности подключения в заглушке.
    /// </summary>
    public bool IsConnected => _timeoutSeconds > 0 && !string.IsNullOrWhiteSpace(_connectionString);

    /// <summary>
    /// Возвращает синтетический набор данных для переданного запроса.
    /// </summary>
    /// <param name="query">SQL-запрос, используется только для формирования демонстрационного script code.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Потоковый ридер с тестовыми строками.</returns>
    public Task<DbDataReader> GetDataReaderAsync(string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetCode = "STUB";
        var scriptCode = string.IsNullOrWhiteSpace(query)
            ? "QUERY"
            : query.Length <= 24
                ? query
                : query[..24];

        var table = new DataTable();
        table.Columns.Add("target_code", typeof(string));
        table.Columns.Add("script", typeof(string));
        table.Columns.Add("row_number", typeof(int));
        table.Columns.Add("event_date_utc", typeof(string));
        table.Columns.Add("amount", typeof(decimal));
        table.Columns.Add("status", typeof(string));

        var rowCount = 2_500;
        for (var i = 1; i <= rowCount; i++)
        {
            table.Rows.Add(
                targetCode,
                scriptCode,
                i,
                DateTime.UtcNow.ToString("O"),
                Math.Round((i * 1.137m) % 995, 2),
                i % 2 == 0 ? "ACTIVE" : "PENDING");
            Thread.Sleep(10);
        }

        DbDataReader reader = table.CreateDataReader();
        return Task.FromResult(reader);
    }

    private static string ResolveConnectionString(string source)
    {
        const string dpapiPrefix = "dpapi:";
        if (!source.StartsWith(dpapiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        var payload = source[dpapiPrefix.Length..];
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Encrypted connection string payload is empty.");
        }

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("DPAPI decryption is supported only on Windows.");
            }

            var encryptedBytes = Convert.FromBase64String(payload);
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var decrypted = Encoding.UTF8.GetString(decryptedBytes);
            if (string.IsNullOrWhiteSpace(decrypted))
            {
                throw new InvalidOperationException("Decrypted connection string is empty.");
            }

            return decrypted;
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Encrypted connection string must contain valid Base64 after 'dpapi:'.", ex);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Failed to decrypt connection string using DPAPI CurrentUser scope.", ex);
        }
    }
}
