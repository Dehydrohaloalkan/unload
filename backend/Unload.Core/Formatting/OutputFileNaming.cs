namespace Unload.Core;

/// <summary>
/// Общие правила формирования имён выходных файлов выгрузки.
/// Используется основной выгрузкой (<c>PipeSeparatedFileChunkWriter</c>) и extra-выгрузкой,
/// чтобы имена чанков строились единообразно.
/// </summary>
public static class OutputFileNaming
{
    private const string Base36Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// Базовое имя файла чанка без расширения: <c>{stem}{dayOfYear:D3}{chunkBase36:2}</c>.
    /// </summary>
    /// <param name="outputFileStem">Префикс имени (первые символы кода скрипта).</param>
    /// <param name="dayOfYear">День года записи.</param>
    /// <param name="chunkNumber">Порядковый номер чанка, начиная с 1.</param>
    public static string BuildChunkBaseName(string outputFileStem, int dayOfYear, int chunkNumber)
    {
        var chunkNumberBase36 = ToBase36(chunkNumber).PadLeft(2, '0');
        return $"{outputFileStem}{dayOfYear:D3}{chunkNumberBase36}";
    }

    /// <summary>
    /// Переводит положительное число в строку системы счисления по основанию 36.
    /// </summary>
    /// <param name="value">Положительное число.</param>
    public static string ToBase36(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Chunk number must be greater than zero.");
        }

        Span<char> buffer = stackalloc char[16];
        var i = buffer.Length;
        var current = value;

        while (current > 0)
        {
            buffer[--i] = Base36Alphabet[current % 36];
            current /= 36;
        }

        return new string(buffer[i..]);
    }

    /// <summary>
    /// Открывает уникальный файл на запись, добавляя суффикс <c>_NN</c> при коллизии имён.
    /// </summary>
    /// <param name="outputDirectory">Директория назначения.</param>
    /// <param name="baseFileName">Базовое имя без расширения.</param>
    /// <param name="extension">Расширение файла (с точкой).</param>
    /// <returns>Поток, имя и полный путь созданного файла.</returns>
    public static (FileStream Stream, string FileName, string FilePath) OpenUniqueFile(
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
}
