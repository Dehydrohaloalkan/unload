namespace Unload.Runner;

/// <summary>
/// Параметры работы движка раннера.
/// </summary>
/// <param name="ChunkSizeBytes">Максимальный размер чанка в байтах.</param>
/// <param name="WorkerCount">Количество worker-потоков (1 клиент БД на поток).</param>
/// <param name="BatchReadMode">Если true: читать все данные в память, передавать на запись без ожидания; клиент сразу выполняет следующий запрос.</param>
public record RunnerOptions(
    int ChunkSizeBytes = 10 * 1024 * 1024,
    int WorkerCount = 4,
    bool BatchReadMode = true);
