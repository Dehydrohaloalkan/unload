using System.Threading;

namespace Unload.Core;

/// <summary>
/// Thread-safe allocator event sequence numbers for one correlation.
/// </summary>
public sealed class RunnerEventSequencer
{
    private long _next;

    public long Next() => Interlocked.Increment(ref _next);
}
