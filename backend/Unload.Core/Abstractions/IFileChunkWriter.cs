namespace Unload.Core;

/// <summary>
/// Контракт записи чанка данных в физический файл.
/// Используется раннером после формирования очередного чанка строк.
/// </summary>
public interface IFileChunkWriter
{
    /// <summary>
    /// Записывает чанк в выходной файл и возвращает метаданные созданного файла.
    /// </summary>
    /// <param name="chunk">Чанк данных, который нужно сохранить.</param>
    /// <param name="outputDirectory">Директория, куда записывается файл.</param>
    /// <param name="cancellationToken">Токен отмены записи.</param>
    /// <returns>Информация о записанном файле и количестве строк.</returns>
    Task<WrittenFile> WriteChunkAsync(
        FileChunk chunk,
        string outputDirectory,
        CancellationToken cancellationToken);
}
