using System.Text;
using Unload.Core;

namespace Unload.FileWriter;

public class PipeSeparatedFileChunkWriter : IFileChunkWriter
{
    public async Task<WrittenFile> WriteChunkAsync(
        FileChunk chunk,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var chunkSuffix = BuildChunkSuffix(chunk.ChunkNumber);
        var fileName = $"{chunk.Script.OutputFileStem}_{chunkSuffix}{chunk.Script.OutputFileExtension}";
        var filePath = Path.Combine(outputDirectory, fileName);

        await using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        var columns = PipeDelimitedFormatter.GetOrderedColumns(chunk.Rows);
        await writer.WriteLineAsync(PipeDelimitedFormatter.BuildHeaderLine(columns));

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

    private static string BuildChunkSuffix(int chunkNumber)
    {
        if (chunkNumber <= 0)
        {
            throw new InvalidOperationException($"Chunk number '{chunkNumber}' is invalid.");
        }

        var value = chunkNumber - 1;
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var result = string.Empty;

        do
        {
            result = alphabet[value % 36] + result;
            value /= 36;
        } while (value > 0);

        return result.Length >= 2 ? result : result.PadLeft(2, '0');
    }
}
