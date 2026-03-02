using Unload.Core;

namespace Unload.Application;

public interface IRunRequestFactory
{
    RunRequest Create(IReadOnlyCollection<string> profileCodes, string outputDirectory);
}
