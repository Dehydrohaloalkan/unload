namespace Unload.Api;

public interface IRunOrchestrator
{
    string StartRun(IReadOnlyCollection<string> profileCodes);
}
