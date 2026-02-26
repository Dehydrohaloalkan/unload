namespace Unload.Core;

public record WrittenFile(
    ScriptDefinition Script,
    int ChunkNumber,
    string FilePath,
    int RowsCount,
    int ByteSize);
