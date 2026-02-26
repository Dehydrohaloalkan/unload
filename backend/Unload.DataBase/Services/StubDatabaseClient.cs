using System.Runtime.CompilerServices;
using Unload.Core;

namespace Unload.DataBase;

public sealed class StubDatabaseClient : IDatabaseClient
{
    public async IAsyncEnumerable<DatabaseRow> ExecuteScriptAsync(
        ScriptDefinition script,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rowCount = 2_500;
        for (var i = 1; i <= rowCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (i % 400 == 0)
            {
                await Task.Delay(5, cancellationToken);
            }

            yield return new DatabaseRow(new Dictionary<string, object?>
            {
                ["profile"] = script.ProfileCode,
                ["script"] = script.ScriptCode,
                ["row_number"] = i,
                ["event_date_utc"] = DateTime.UtcNow.ToString("O"),
                ["amount"] = Math.Round((i * 1.137m) % 995, 2),
                ["status"] = i % 2 == 0 ? "ACTIVE" : "PENDING"
            });
        }
    }
}
