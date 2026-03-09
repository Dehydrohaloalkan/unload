using System.Collections.Concurrent;
using System.Text;
using Unload.Core;

namespace Unload.FileWriter;

/// <summary>
/// Реализация записи чанка в pipe-delimited файл.
/// Используется раннером для сохранения результатов SQL-скриптов по частям.
/// </summary>
public class PipeSeparatedFileChunkWriter : IFileChunkWriter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileWriteLocks =
        new(StringComparer.OrdinalIgnoreCase);

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
        var chunkNumberBase36 = ToBase36(chunk.ChunkNumber).PadLeft(2, '0');
        var baseFileName = $"{chunk.Script.OutputFileStem}{dayOfYear:D3}{chunkNumberBase36}";
        var fileExtension = chunk.Script.OutputFileExtension;
        var lockKey = Path.GetFullPath(Path.Combine(outputDirectory, $"{baseFileName}{fileExtension}"));
        var fileLock = FileWriteLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));

        await fileLock.WaitAsync(cancellationToken);
        try
        {
            var (stream, fileName, filePath) = OpenUniqueFile(outputDirectory, baseFileName, fileExtension);
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
        finally
        {
            fileLock.Release();
        }
    }

    private static (FileStream Stream, string FileName, string FilePath) OpenUniqueFile(
        string outputDirectory,
        string baseFileName,
        string extension)
    {
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : $"_{attempt:D2}";
            var fileName = $"{baseFileName}{suffix}{extension}";
            var filePath = Path.Combine(outputDirectory, fileName);
            try
            {
                var stream = File.Open(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return (stream, fileName, filePath);
            }
            catch (IOException)
            {
            }
        }

        throw new IOException(
            $"Unable to allocate unique output file name for base '{baseFileName}{extension}' in '{outputDirectory}'.");
    }

    private static string ToBase36(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Chunk number must be greater than zero.");
        }

        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Span<char> buffer = stackalloc char[16];
        var i = buffer.Length;
        var current = value;

        while (current > 0)
        {
            buffer[--i] = alphabet[current % 36];
            current /= 36;
        }

        return new string(buffer[i..]);
    }
}
