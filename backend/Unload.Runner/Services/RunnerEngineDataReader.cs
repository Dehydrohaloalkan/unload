using System.Data.Common;
using Unload.Core;

namespace Unload.Runner;

/// <summary>
/// Вспомогательные операции чтения данных из <see cref="DbDataReader"/>.
/// </summary>
internal static class RunnerEngineDataReader
{
    public static List<string> GetColumns(DbDataReader reader)
    {
        var columns = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        return columns;
    }

    public static DatabaseRow ReadRow(DbDataReader reader, IReadOnlyList<string> columns)
    {
        var values = new Dictionary<string, object?>(columns.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
        {
            values[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return new DatabaseRow(values);
    }
}
