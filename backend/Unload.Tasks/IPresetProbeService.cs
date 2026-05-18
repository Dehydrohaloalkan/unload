namespace Unload.Tasks;

/// <summary>
/// Единая логика выполнения preset probe и применения результата в <see cref="DailyWindowPolicy"/>.
/// Используется API и Console. Probe станет обычной задачей в Фазе 5.
/// </summary>
public interface IPresetProbeService
{
    /// <summary>
    /// Выполняет probe SQL из <see cref="PresetGateOptions"/> и применяет результат
    /// к <see cref="DailyWindowPolicy"/>.
    /// </summary>
    /// <returns>Числовое значение probe (обычно 0/1).</returns>
    Task<int> ExecuteAndApplyAsync(CancellationToken cancellationToken);
}
