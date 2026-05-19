namespace Unload.Tasks;

/// <summary>
/// Генерация корреляционных идентификаторов исполнений задач выгрузки.
/// Единая реализация для probe/preset/extra (раньше дублировалась в каждой задаче).
/// </summary>
public static class TaskCorrelationId
{
    /// <summary>
    /// Фиксированная длина идентификатора: префикс + время (17 цифр) + усечённый GUID.
    /// </summary>
    private const int MaxLength = 43;

    /// <summary>
    /// Создаёт идентификатор вида <c>{prefix}-{yyyyMMddHHmmssfff}-{guid}</c>,
    /// усечённый до <see cref="MaxLength"/> символов.
    /// </summary>
    /// <param name="prefix">Префикс задачи (например, <c>preset</c>).</param>
    public static string Create(string prefix)
    {
        var raw = $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        return raw.Length <= MaxLength ? raw : raw[..MaxLength];
    }
}
