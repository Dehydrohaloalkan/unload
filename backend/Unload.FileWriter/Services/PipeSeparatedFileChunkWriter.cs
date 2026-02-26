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
        var profileDirectory = Path.Combine(outputDirectory, chunk.Script.ProfileCode);
        Directory.CreateDirectory(profileDirectory);

        var fileName = $"{chunk.Script.ScriptCode}_{chunk.ChunkNumber:D4}.txt";
        var filePath = Path.Combine(profileDirectory, fileName);

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
}
