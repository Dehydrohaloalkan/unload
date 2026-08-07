namespace Unload.Store;

public enum PersistenceHealthStatus
{
    Healthy,
    Recovered,
    Degraded,
    Corrupted
}

public sealed record PersistenceHealthInfo(
    PersistenceHealthStatus Status,
    bool IsWritable,
    DateTimeOffset? ChangedAt,
    string? FailureType);
