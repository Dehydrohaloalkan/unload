namespace Unload.Store;

public sealed class PersistenceUnavailableException : InvalidOperationException
{
    public PersistenceUnavailableException(string filePath, Exception persistenceFailure)
        : base(
            $"Persistence store '{filePath}' is unavailable after a previous persistence failure.",
            persistenceFailure)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
}
