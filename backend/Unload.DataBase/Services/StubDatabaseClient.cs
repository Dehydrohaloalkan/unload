using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Unload.Core;

namespace Unload.DataBase;

/// <summary>
/// Заглушка клиента БД, генерирующая тестовый <see cref="DbDataReader"/>.
/// Используется в development/demo режиме вместо реального подключения к базе данных.
/// </summary>
public class StubDatabaseClient : IDatabaseClient
{
    /// <summary>Количество синтетических строк в демонстрационном наборе данных.</summary>
    private const int StubRowCount = 2_500;

    /// <summary>Задержка на строку, имитирующая латентность БД, мс.</summary>
    private const int StubRowDelayMs = 10;

    /// <summary>Максимальная длина демонстрационного script-кода, выводимого из запроса.</summary>
    private const int ScriptCodeMaxLength = 24;

    /// <summary>Количество синтетических строк на банк в extra-наборе.</summary>
    private const int StubExtraRowsPerBank = 50;

    /// <summary>
    /// Задержка на строку extra-набора, мс. Имитирует «большой и долгий» extra-скрипт
    /// (по умолчанию extra-стаб отрабатывал мгновенно): 6 банков × 50 строк × 60мс ≈ 18с на скрипт.
    /// Отмена проверяется в цикле, поэтому «Стоп» прерывает выгрузку, не дожидаясь конца набора.
    /// </summary>
    private const int StubExtraRowDelayMs = 60;

    /// <summary>Демонстрационный справочник банков (NrBank → читаемое имя).</summary>
    private static readonly (string NrBank, string BankName)[] StubBanks =
    [
        ("B01", "Альфа-Банк"),
        ("B02", "Бета-Банк"),
        ("B03", "Гамма-Банк"),
        ("B04", "Дельта-Банк"),
        ("B05", "Эпсилон-Банк"),
        ("B06", "Дзета-Банк"),
    ];

    private static readonly Regex InClauseQuotedValues = new(
        @"IN\s*\((?<values>[^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex QuotedValue = new(
        "'(?<value>(?:[^']|'')*)'",
        RegexOptions.Compiled);

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
        if (!string.IsNullOrWhiteSpace(query) &&
            query.Contains("PRESET_READY_PROBE", StringComparison.OrdinalIgnoreCase))
        {
            var probeTable = new DataTable();
            probeTable.Columns.Add("Value", typeof(int));
            probeTable.Rows.Add(Random.Shared.Next(2));
            DbDataReader probeReader = probeTable.CreateDataReader();
            return Task.FromResult(probeReader);
        }

        if (!string.IsNullOrWhiteSpace(query) &&
            query.Contains("EXTRA_BANKS", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(CreateExtraBanksReader());
        }

        if (!string.IsNullOrWhiteSpace(query) &&
            query.Contains("EXTRA_UNLOAD", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(CreateExtraUnloadReader(query, cancellationToken));
        }

        var targetCode = "STUB";
        var scriptCode = string.IsNullOrWhiteSpace(query)
            ? "QUERY"
            : query.Length <= ScriptCodeMaxLength
                ? query
                : query[..ScriptCodeMaxLength];

        var table = new DataTable();
        table.Columns.Add("target_code", typeof(string));
        table.Columns.Add("script", typeof(string));
        table.Columns.Add("row_number", typeof(int));
        table.Columns.Add("event_date_utc", typeof(string));
        table.Columns.Add("amount", typeof(decimal));
        table.Columns.Add("status", typeof(string));

        for (var i = 1; i <= StubRowCount; i++)
        {
            table.Rows.Add(
                targetCode,
                scriptCode,
                i,
                DateTime.UtcNow.ToString("O"),
                Math.Round((i * 1.137m) % 995, 2),
                i % 2 == 0 ? "ACTIVE" : "PENDING");
            Thread.Sleep(StubRowDelayMs);
        }

        DbDataReader reader = table.CreateDataReader();
        return Task.FromResult(reader);
    }

    /// <summary>Справочник банков для extra-выгрузки (маркер <c>EXTRA_BANKS</c>).</summary>
    private static DbDataReader CreateExtraBanksReader()
    {
        var table = new DataTable();
        table.Columns.Add("NrBank", typeof(string));
        table.Columns.Add("BankName", typeof(string));
        foreach (var (nrBank, bankName) in StubBanks)
        {
            table.Rows.Add(nrBank, bankName);
        }

        return table.CreateDataReader();
    }

    /// <summary>
    /// Данные extra-скрипта (маркер <c>EXTRA_UNLOAD</c>): колонки NrBank + LineFile.
    /// Если в тексте присутствует <c>IN ('B01',...)</c> — эмитятся только перечисленные банки,
    /// чтобы снятие банков в UI реально уменьшало вывод.
    /// </summary>
    private static DbDataReader CreateExtraUnloadReader(string query, CancellationToken cancellationToken)
    {
        var allowedBanks = ParseInClauseBanks(query);

        var table = new DataTable();
        table.Columns.Add("NrBank", typeof(string));
        table.Columns.Add("LineFile", typeof(string));

        foreach (var (nrBank, bankName) in StubBanks)
        {
            if (allowedBanks is not null && !allowedBanks.Contains(nrBank))
            {
                continue;
            }

            for (var i = 1; i <= StubExtraRowsPerBank; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lineFile = string.Join(
                    '|',
                    nrBank,
                    bankName,
                    i,
                    DateTime.UtcNow.ToString("O"),
                    Math.Round((i * 1.137m) % 995, 2),
                    i % 2 == 0 ? "ACTIVE" : "PENDING");
                table.Rows.Add(nrBank, lineFile);
                // Имитация латентности «долгого» extra-скрипта (как у основного run-стаба).
                Thread.Sleep(StubExtraRowDelayMs);
            }
        }

        return table.CreateDataReader();
    }

    /// <summary>
    /// Извлекает коды банков из конструкции <c>IN ('B01','B02')</c>; возвращает <c>null</c>,
    /// если конструкции нет (фильтр не применяется).
    /// </summary>
    private static HashSet<string>? ParseInClauseBanks(string query)
    {
        var match = InClauseQuotedValues.Match(query);
        if (!match.Success)
        {
            return null;
        }

        var banks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match value in QuotedValue.Matches(match.Groups["values"].Value))
        {
            var bank = value.Groups["value"].Value.Replace("''", "'").Trim();
            if (!string.IsNullOrEmpty(bank))
            {
                banks.Add(bank);
            }
        }

        return banks.Count > 0 ? banks : null;
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
