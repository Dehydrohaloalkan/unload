namespace Unload.Store;

public sealed class PersistenceUnavailableException : InvalidOperationException
{
    public PersistenceUnavailableException(string filePath, Exception previousWriteFailure)
        : base(
            $"Persistence store '{filePath}' is unavailable after a previous write failure.",
            previousWriteFailure)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
}
