namespace Unload.Core;

public sealed record RunRequest(
    IReadOnlyCollection<string> ProfileCodes,
    string CorrelationId,
    string OutputDirectory);
