namespace Unload.Runner;

/// <summary>
/// Параметры работы движка раннера.
/// </summary>
/// <param name="ChunkSizeBytes">Максимальный размер чанка в байтах.</param>
/// <param name="WorkerCount">Количество worker-потоков (1 клиент БД на поток).</param>
public record RunnerOptions(
    int ChunkSizeBytes = 10 * 1024 * 1024,
    int WorkerCount = 4);
