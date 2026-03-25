using Unload.Core;
using Unload.Workflow;
using Microsoft.Extensions.Logging;

namespace Unload.TaskFlow;

public sealed class PresetProbeService : IPresetProbeService
{
    private readonly PresetGateOptions _options;
    private readonly IPresetGateService _presetGateService;
    private readonly IWorkflowStageStateStore _workflowStageStateStore;
    private readonly IDatabaseClientFactory _databaseClientFactory;
    private readonly ILogger<PresetProbeService> _logger;

    public PresetProbeService(
        PresetGateOptions options,
        IPresetGateService presetGateService,
        IWorkflowStageStateStore workflowStageStateStore,
        IDatabaseClientFactory databaseClientFactory,
        ILogger<PresetProbeService> logger)
    {
        _options = options;
        _presetGateService = presetGateService;
        _workflowStageStateStore = workflowStageStateStore;
        _databaseClientFactory = databaseClientFactory;
        _logger = logger;
    }

    public async Task<int> ExecuteAndApplyAsync(CancellationToken cancellationToken)
    {
        var probeResult = await ProbeAsync(cancellationToken);

        _presetGateService.StartPolling();
        var changed = _presetGateService.ApplyProbeResult(probeResult, DateTimeOffset.UtcNow);
        if (probeResult == 1)
        {
            _workflowStageStateStore.MarkCompleted(WorkflowStageCodes.PresetProbeReady);
        }

        if (changed)
        {
            _logger.LogInformation("Preset probe applied. ProbeResult: {ProbeResult}", probeResult);
        }

        return probeResult;
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

