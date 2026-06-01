using System.Data.Common;
using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Возвращает справочник банков (NrBank + читаемое BankName) для выбора в настройках extra-выгрузки.
/// Источник — зарезервированный скрипт <c>scripts/extra/_banks.sql</c>.
/// </summary>
public class ExtraBanksService(
    ExtraUnloadOptions options,
    IDatabaseClientFactory databaseClientFactory,
    ILogger<ExtraBanksService> logger)
{
    private readonly ExtraUnloadOptions _options = options;
    private readonly IDatabaseClientFactory _databaseClientFactory = databaseClientFactory;
    private readonly ILogger<ExtraBanksService> _logger = logger;

    public async Task<IReadOnlyList<ExtraBankInfo>> GetBanksAsync(CancellationToken cancellationToken)
    {
        var banksScriptPath = _options.BanksScriptPath;
        if (!File.Exists(banksScriptPath))
        {
            throw new FileNotFoundException($"Banks reference script was not found: {banksScriptPath}", banksScriptPath);
        }

        var sql = await File.ReadAllTextAsync(banksScriptPath, cancellationToken);
        var banks = new List<ExtraBankInfo>();

        var client = _databaseClientFactory.CreateClient();
        try
        {
            await using var reader = await client.GetDataReaderAsync(sql, cancellationToken);
            var nrBankOrdinal = ResolveOrdinal(reader, "NrBank", 0);
            var bankNameOrdinal = ResolveOrdinal(reader, "BankName", 1);

            while (await reader.ReadAsync(cancellationToken))
            {
                var nrBank = reader.IsDBNull(nrBankOrdinal)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(nrBankOrdinal)) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(nrBank))
                {
                    continue;
                }

                var bankName = reader.IsDBNull(bankNameOrdinal)
                    ? nrBank
                    : Convert.ToString(reader.GetValue(bankNameOrdinal)) ?? nrBank;

                banks.Add(new ExtraBankInfo(nrBank.Trim(), bankName.Trim()));
            }
        }
        finally
        {
            await DisposeDatabaseClientAsync(client);
        }

        _logger.LogInformation("Extra banks resolved. Count: {Count}", banks.Count);
        return banks
            .DistinctBy(static b => b.NrBank, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static b => b.BankName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int ResolveOrdinal(DbDataReader reader, string columnName, int fallbackOrdinal)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        if (fallbackOrdinal < reader.FieldCount)
        {
            return fallbackOrdinal;
        }

        throw new InvalidOperationException($"Result set does not contain required column '{columnName}'.");
    }

    private static async Task DisposeDatabaseClientAsync(IDatabaseClient client)
    {
        if (client is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
            return;
        }

        if (client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
