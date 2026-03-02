using Unload.Core;

namespace Unload.Api;

public class RunRequestFactory : IRunRequestFactory
{
    public RunRequest Create(IReadOnlyCollection<string> profileCodes, string outputDirectory)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return new RunRequest(
            profileCodes,
            CorrelationId: $"req-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{suffix}",
            OutputDirectory: outputDirectory);
    }
}
