namespace Unload.Runner;

public sealed record RunnerOptions(
    int ChunkSizeBytes,
    int MaxDegreeOfParallelism,
    int DataflowBoundedCapacity);
