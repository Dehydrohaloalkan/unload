namespace Unload.Core;

public sealed record DatabaseRow(
    IReadOnlyDictionary<string, object?> Columns);
