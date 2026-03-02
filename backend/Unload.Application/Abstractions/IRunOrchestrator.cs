namespace Unload.Application;

public interface IRunOrchestrator
{
    string StartRun(IReadOnlyCollection<string> profileCodes);
}
