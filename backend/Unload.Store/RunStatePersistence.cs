using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Unload.Store;

internal sealed class RunStatePersistence
{
    private const string PersistenceVersion = "1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _writerSync = new();
    private readonly JsonFileStore<RunStatePersistenceSnapshot> _store;

    public RunStatePersistence(string stateFilePath, ILogger? logger = null)
    {
        _store = new JsonFileStore<RunStatePersistenceSnapshot>(stateFilePath, JsonOptions, logger);
    }

    public RunStatePersistenceSnapshot? Load()
    {
        return _store.Load();
    }

    public void EnsureWritable()
    {
        _store.EnsureWritable();
    }

    public PersistenceHealthInfo GetHealth()
    {
        return _store.GetHealth();
    }

    public void Save(Func<IReadOnlyCollection<RunStatusInfo>> captureRuns)
    {
        ArgumentNullException.ThrowIfNull(captureRuns);

        lock (_writerSync)
        {
            var snapshot = new RunStatePersistenceSnapshot(
                PersistenceVersion,
                DateTimeOffset.UtcNow,
                captureRuns());
            _store.Save(snapshot);
        }
    }
}

internal sealed record RunStatePersistenceSnapshot(
    string Version,
    DateTimeOffset SavedAt,
    IReadOnlyCollection<RunStatusInfo> Runs);
