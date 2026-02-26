namespace Unload.Runner;

public record RunnerOptions(
    int ChunkSizeBytes,
    int MaxDegreeOfParallelism,
    int DataflowBoundedCapacity);
