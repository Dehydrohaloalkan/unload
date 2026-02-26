namespace Unload.Core;

public record FileChunk(
    ScriptDefinition Script,
    int ChunkNumber,
    IReadOnlyList<DatabaseRow> Rows,
    int ByteSize);
