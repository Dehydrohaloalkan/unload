using Unload.Core;

namespace Unload.Api;

public interface IRunRequestFactory
{
    RunRequest Create(IReadOnlyCollection<string> profileCodes, string outputDirectory);
}
