namespace Unload.Core;

public record RunRequest(
    IReadOnlyCollection<string> ProfileCodes,
    string CorrelationId,
    string OutputDirectory);
