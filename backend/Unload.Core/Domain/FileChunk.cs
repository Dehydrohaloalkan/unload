namespace Unload.Core;

/// <summary>
/// Описывает порцию данных скрипта для записи в отдельный файл.
/// Используется раннером как буфер между чтением БД и файловым writer.
/// </summary>
/// <param name="Script">Скрипт, к которому относится чанк.</param>
/// <param name="ChunkNumber">Порядковый номер чанка, начиная с 1.</param>
/// <param name="Rows">Содержимое чанка в виде строк данных.</param>
/// <param name="ByteSize">Оценочный размер чанка в байтах с учетом строк и заголовка.</param>
public record FileChunk(
    ScriptDefinition Script,
    int ChunkNumber,
    IReadOnlyList<DatabaseRow> Rows,
    int ByteSize);
