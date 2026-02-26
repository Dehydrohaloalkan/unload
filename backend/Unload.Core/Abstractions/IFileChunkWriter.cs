namespace Unload.Core;

public interface IFileChunkWriter
{
    Task<WrittenFile> WriteChunkAsync(
        FileChunk chunk,
        string outputDirectory,
        CancellationToken cancellationToken);
}
