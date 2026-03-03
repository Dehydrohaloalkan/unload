namespace Unload.Runner;

/// <summary>
/// Параметры работы движка раннера.
/// Используется для настройки разбиения на чанки и степени параллелизма обработки скриптов.
/// </summary>
/// <param name="ChunkSizeBytes">Максимальный размер чанка в байтах.</param>
/// <param name="MaxDegreeOfParallelism">Максимальное количество скриптов, выполняемых одновременно.</param>
/// <param name="DataflowBoundedCapacity">Емкость внутренних буферов канала событий.</param>
public record RunnerOptions(
    int ChunkSizeBytes,
    int MaxDegreeOfParallelism,
    int DataflowBoundedCapacity);
