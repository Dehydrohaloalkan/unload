namespace Unload.Runner;

/// <summary>
/// Параметры работы движка раннера.
/// Используется для настройки разбиения на чанки и степени параллелизма обработки скриптов.
/// </summary>
/// <param name="ChunkSizeBytes">Максимальный размер чанка в байтах.</param>
/// <param name="MaxDegreeOfParallelism">Количество worker-ов этапа чтения SQL и формирования чанков.</param>
/// <param name="FileWriterDegreeOfParallelism">Количество worker-ов этапа записи чанков в файлы.</param>
/// <param name="QueuePublisherDegreeOfParallelism">Количество worker-ов этапа публикации событий в MQ/канал.</param>
/// <param name="DataflowBoundedCapacity">Емкость внутренних буферов Dataflow-блоков.</param>
public record RunnerOptions(
    int ChunkSizeBytes,
    int MaxDegreeOfParallelism,
    int FileWriterDegreeOfParallelism,
    int QueuePublisherDegreeOfParallelism,
    int DataflowBoundedCapacity);
