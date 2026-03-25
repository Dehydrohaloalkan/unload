using Unload.Workflow;

namespace Unload.TaskFlow;

/// <summary>
/// Единая логика выполнения preset probe и применения результата в preset-gate + workflow-stage.
/// Используется и API, и Console.
/// </summary>
public interface IPresetProbeService
{
    /// <summary>
    /// Выполняет probe SQL из <see cref="PresetGateOptions"/> и применяет результат:
    /// обновляет <see cref="IPresetGateService"/> и отмечает стадию <see cref="WorkflowStageCodes.PresetProbeReady"/> при результате 1.
    /// </summary>
    /// <returns>Числовое значение probe (обычно 0/1).</returns>
    Task<int> ExecuteAndApplyAsync(CancellationToken cancellationToken);
}

