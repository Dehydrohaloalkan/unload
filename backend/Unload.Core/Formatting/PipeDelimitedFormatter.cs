using System.Text;

namespace Unload.Core;

/// <summary>
/// Утилита формирования строк в pipe-delimited формате.
/// Используется раннером и writer для единообразного построения заголовков и строк данных.
/// </summary>
public static class PipeDelimitedFormatter
{
    /// <summary>
    /// Формирует упорядоченный список колонок в порядке первого появления в данных.
    /// </summary>
    /// <param name="rows">Строки данных, из которых извлекаются имена колонок.</param>
    /// <returns>Список уникальных имен колонок.</returns>
    public static IReadOnlyList<string> GetOrderedColumns(IReadOnlyList<DatabaseRow> rows)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            foreach (var key in row.Columns.Keys)
            {
                if (seen.Add(key))
                {
                    ordered.Add(key);
                }
            }
        }

        return ordered;
    }

    /// <summary>
    /// Строит заголовок файла в формате pipe-delimited.
    /// </summary>
    /// <param name="columns">Имена колонок в требуемом порядке.</param>
    /// <returns>Строка заголовка для первой строки выходного файла.</returns>
    public static string BuildHeaderLine(IReadOnlyList<string> columns)
    {
        return string.Join('|', columns.Select(Escape));
    }

    /// <summary>
    /// Строит строку данных в формате pipe-delimited по заданному порядку колонок.
    /// </summary>
    /// <param name="row">Источник значений строки.</param>
    /// <param name="columns">Порядок колонок, соответствующий заголовку файла.</param>
    /// <returns>Сериализованная строка данных с экранированием спецсимволов.</returns>
    public static string BuildDataLine(DatabaseRow row, IReadOnlyList<string> columns)
    {
        var values = new string[columns.Count];

        for (var i = 0; i < columns.Count; i++)
        {
            var key = columns[i];
            row.Columns.TryGetValue(key, out var rawValue);
            values[i] = Escape(rawValue?.ToString() ?? string.Empty);
        }

        return string.Join('|', values);
    }

    /// <summary>
    /// Оценивает размер строки в байтах при UTF-8 кодировке с переводом строки.
    /// </summary>
    /// <param name="line">Строка для оценки.</param>
    /// <returns>Количество байт, которое будет записано в файл.</returns>
    public static int EstimateLineBytes(string line)
    {
        return Encoding.UTF8.GetByteCount(line) + 1;
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
