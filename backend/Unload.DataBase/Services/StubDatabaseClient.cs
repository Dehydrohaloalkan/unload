using System.Data;
using System.Data.Common;
using Unload.Core;

namespace Unload.DataBase;

public class StubDatabaseClient : IDatabaseClient
{
    public bool IsConnected => true;

    public Task<DbDataReader> GetDataReaderAsync(string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var profileCode = "STUB";
        var scriptCode = string.IsNullOrWhiteSpace(query)
            ? "QUERY"
            : query.Length <= 24
                ? query
                : query[..24];

        var table = new DataTable();
        table.Columns.Add("profile", typeof(string));
        table.Columns.Add("script", typeof(string));
        table.Columns.Add("row_number", typeof(int));
        table.Columns.Add("event_date_utc", typeof(string));
        table.Columns.Add("amount", typeof(decimal));
        table.Columns.Add("status", typeof(string));

        var rowCount = 2_500;
        for (var i = 1; i <= rowCount; i++)
        {
            table.Rows.Add(
                profileCode,
                scriptCode,
                i,
                DateTime.UtcNow.ToString("O"),
                Math.Round((i * 1.137m) % 995, 2),
                i % 2 == 0 ? "ACTIVE" : "PENDING");
        }

        DbDataReader reader = table.CreateDataReader();
        return Task.FromResult(reader);
    }
}
