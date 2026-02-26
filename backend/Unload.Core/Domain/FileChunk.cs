namespace Unload.Core;

public sealed record FileChunk(
    ScriptDefinition Script,
    int ChunkNumber,
    IReadOnlyList<DatabaseRow> Rows,
    int ByteSize);
