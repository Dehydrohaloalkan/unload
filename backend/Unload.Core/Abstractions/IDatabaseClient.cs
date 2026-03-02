using System.Data.Common;

namespace Unload.Core;

public interface IDatabaseClient
{
    bool IsConnected { get; }

    Task<DbDataReader> GetDataReaderAsync(string query, CancellationToken cancellationToken = default);
}
