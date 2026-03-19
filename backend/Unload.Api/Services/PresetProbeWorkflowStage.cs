using Unload.Core;
using Unload.TaskFlow;

namespace Unload.Api;

/// <summary>
/// Системная workflow-стадия проверки готовности preset через probe SQL.
/// Отвечает только за выполнение probe и фиксацию stage-state, без расписания.
/// </summary>
public interface IPresetProbeWorkflowStage
{
    /// <summary>
    /// Выполняет probe и обновляет состояние preset-гейта и workflow-stage.
    /// </summary>
    /// <returns><c>true</c>, если состояние изменилось.</returns>
    Task<bool> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class PresetProbeWorkflowStage : IPresetProbeWorkflowStage
{
    private readonly PresetGateOptions _options;
    private readonly IPresetGateService _presetGateService;
    private readonly IWorkflowStageStateStore _workflowStageStateStore;
    private readonly IDatabaseClientFactory _databaseClientFactory;
    private readonly ILogger<PresetProbeWorkflowStage> _logger;

    public PresetProbeWorkflowStage(
        PresetGateOptions options,
        IPresetGateService presetGateService,
        IWorkflowStageStateStore workflowStageStateStore,
        IDatabaseClientFactory databaseClientFactory,
        ILogger<PresetProbeWorkflowStage> logger)
    {
        _options = options;
        _presetGateService = presetGateService;
        _workflowStageStateStore = workflowStageStateStore;
        _databaseClientFactory = databaseClientFactory;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        var probeResult = await ProbeAsync(cancellationToken);
        var changed = _presetGateService.ApplyProbeResult(probeResult, DateTimeOffset.UtcNow);
        if (probeResult == 1)
        {
            _workflowStageStateStore.MarkCompleted(WorkflowStageCodes.PresetProbeReady);
        }

        if (changed)
        {
            _logger.LogInformation("Preset probe stage state changed. ProbeResult: {ProbeResult}", probeResult);
        }

        return changed;
    }

    private async Task<int> ProbeAsync(CancellationToken cancellationToken)
    {
        var client = _databaseClientFactory.CreateClient();
        try
        {
            await using var reader = await client.GetDataReaderAsync(_options.ProbeSql, cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return 0;
            }

            if (reader.FieldCount == 0 || reader.IsDBNull(0))
            {
                return 0;
            }

            return Convert.ToInt32(reader.GetValue(0));
        }
        finally
        {
            if (client is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
