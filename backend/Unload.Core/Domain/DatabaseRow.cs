namespace Unload.Core;

/// <summary>
/// Представляет одну строку данных, прочитанную из <c>DbDataReader</c>.
/// Используется при сборке чанков и записи строк в выходной файл.
/// </summary>
/// <param name="Columns">Набор значений колонки: имя колонки -> значение (или <c>null</c>).</param>
public record DatabaseRow(
    IReadOnlyDictionary<string, object?> Columns);
