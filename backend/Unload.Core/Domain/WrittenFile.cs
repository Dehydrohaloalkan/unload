namespace Unload.Core;

public sealed record WrittenFile(
    ScriptDefinition Script,
    int ChunkNumber,
    string FilePath,
    int RowsCount,
    int ByteSize);
