namespace Unload.Core;

/// <summary>
/// Содержит результат фактической записи чанка в файл.
/// Используется раннером для публикации событий по записанному файлу.
/// </summary>
/// <param name="Script">Скрипт, из которого сформирован файл.</param>
/// <param name="ChunkNumber">Номер записанного чанка.</param>
/// <param name="FilePath">Полный путь к созданному файлу.</param>
/// <param name="RowsCount">Количество строк данных, записанных в файл.</param>
/// <param name="ByteSize">Размер записанного чанка в байтах.</param>
public record WrittenFile(
    ScriptDefinition Script,
    int ChunkNumber,
    string FilePath,
    int RowsCount,
    int ByteSize);
