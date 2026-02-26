using Unload.Core;

namespace Unload.Api;

public sealed class RunRequestFactory : IRunRequestFactory
{
    public RunRequest Create(IReadOnlyCollection<string> profileCodes, string outputDirectory)
    {
        return new RunRequest(
            profileCodes,
            CorrelationId: $"req-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            OutputDirectory: outputDirectory);
    }
}
