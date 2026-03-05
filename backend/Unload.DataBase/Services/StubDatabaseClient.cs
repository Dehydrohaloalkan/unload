using System.Data;
using System.Data.Common;
using Unload.Core;

namespace Unload.DataBase;

/// <summary>
/// Заглушка клиента БД, генерирующая тестовый <see cref="DbDataReader"/>.
/// Используется в development/demo режиме вместо реального подключения к базе данных.
/// </summary>
public class StubDatabaseClient : IDatabaseClient
{
    /// <summary>
    /// Всегда сообщает о доступности подключения в заглушке.
    /// </summary>
    public bool IsConnected => true;

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
}
