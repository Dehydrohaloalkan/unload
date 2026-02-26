namespace Unload.Core;

public record DatabaseRow(
    IReadOnlyDictionary<string, object?> Columns);
