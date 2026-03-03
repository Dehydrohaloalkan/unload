using System.Text;
using Unload.Core;

namespace Unload.FileWriter;

/// <summary>
/// Реализация записи чанка в pipe-delimited файл.
/// Используется раннером для сохранения результатов SQL-скриптов по частям.
/// </summary>
public class PipeSeparatedFileChunkWriter : IFileChunkWriter
{
    /// <summary>
    /// Записывает служебный заголовок и строки чанка в выходной файл нового формата.
    /// </summary>
    /// <param name="chunk">Чанк данных для записи.</param>
    /// <param name="outputDirectory">Директория назначения.</param>
    /// <param name="cancellationToken">Токен отмены записи.</param>
    /// <returns>Информация о записанном файле.</returns>
    public async Task<WrittenFile> WriteChunkAsync(
        FileChunk chunk,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var dayOfYear = DateTimeOffset.Now.DayOfYear;
        var fileName = $"{chunk.Script.OutputFileStem}{dayOfYear}{chunk.ChunkNumber}{chunk.Script.OutputFileExtension}";
        var filePath = Path.Combine(outputDirectory, fileName);

        await using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        var columns = PipeDelimitedFormatter.GetOrderedColumns(chunk.Rows);
        var metadataHeader =
            $"#|{chunk.Script.ScriptType}|{fileName}|{OutputFormatConstants.SenderCode}|{DateTimeOffset.Now:yyyy-MM-dd}|{chunk.Rows.Count}|{chunk.Script.FirstCodeDigit}";
        await writer.WriteLineAsync(metadataHeader);

        foreach (var row in chunk.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(PipeDelimitedFormatter.BuildDataLine(row, columns));
        }

        await writer.FlushAsync(cancellationToken);

        return new WrittenFile(
            chunk.Script,
            chunk.ChunkNumber,
            filePath,
            chunk.Rows.Count,
            chunk.ByteSize);
    }
}
