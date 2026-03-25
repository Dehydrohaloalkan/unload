using Microsoft.AspNetCore.SignalR;
using Unload.TaskFlow;

namespace Unload.Api;

/// <summary>
/// Фоновая задача проверки доступности preset-этапа по расписанию.
/// </summary>
public sealed class PresetGateBackgroundService : BackgroundService
{
    private readonly PresetGateOptions _options;
    private readonly IPresetGateService _presetGateService;
    private readonly IWorkflowTaskAccessService _workflowTaskAccessService;
    private readonly IWorkflowStageStateStore _workflowStageStateStore;
    private readonly IPresetProbeService _presetProbeService;
    private readonly IHubContext<RunStatusHub> _hubContext;
    private readonly ILogger<PresetGateBackgroundService> _logger;

    public PresetGateBackgroundService(
        PresetGateOptions options,
        IPresetGateService presetGateService,
        IWorkflowTaskAccessService workflowTaskAccessService,
        IWorkflowStageStateStore workflowStageStateStore,
        IPresetProbeService presetProbeService,
        IHubContext<RunStatusHub> hubContext,
        ILogger<PresetGateBackgroundService> logger)
    {
        _options = options;
        _presetGateService = presetGateService;
        _workflowTaskAccessService = workflowTaskAccessService;
        _workflowStageStateStore = workflowStageStateStore;
        _presetProbeService = presetProbeService;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _presetGateService.ApplyInitialOptions(_options);
        _workflowTaskAccessService.ResetCompletedTasks();
        _workflowStageStateStore.Reset();
        _logger.LogInformation(
            "Preset gate service initialized. Enabled: {Enabled}, Start: {StartHour:D2}:{StartMinute:D2}, PollIntervalSeconds: {PollIntervalSeconds}",
            _options.Enabled,
            Clamp(_options.StartHour, 0, 23),
            Clamp(_options.StartMinute, 0, 59),
            Math.Max(5, _options.PollIntervalSeconds));
        await PublishStateAsync(stoppingToken);

        var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.PollIntervalSeconds)));
        try
        {
            await CheckAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await CheckAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Preset gate service stopping.");
        }
        finally
        {
            timer.Dispose();
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (_presetGateService.RefreshDailyWindowState())
        {
            _workflowTaskAccessService.ResetCompletedTasks();
            _workflowStageStateStore.Reset();
            _logger.LogInformation("Preset gate daily window state updated.");
            await PublishStateAsync(cancellationToken);
        }

        if (!_options.Enabled)
        {
            return;
        }

        var now = DateTime.Now;
        var localStartTime = new TimeOnly(
            Clamp(_options.StartHour, 0, 23),
            Clamp(_options.StartMinute, 0, 59));
        if (TimeOnly.FromDateTime(now) < localStartTime)
        {
            return;
        }

        if (_presetGateService.StartPolling())
        {
            _logger.LogInformation("Preset gate polling started.");
            await PublishStateAsync(cancellationToken);
        }

        var state = _presetGateService.Get();
        if (state.PresetCompleted || state.ReadyForPreset)
        {
            return;
        }

        try
        {
            var previous = _presetGateService.Get();
            await _presetProbeService.ExecuteAndApplyAsync(cancellationToken);
            var current = _presetGateService.Get();

            if (!Equals(previous, current))
                await PublishStateAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preset probe failed.");
        }
    }

    private async Task PublishStateAsync(CancellationToken cancellationToken)
    {
        await _hubContext.Clients.All.SendAsync("preset_state", _presetGateService.Get(), cancellationToken);
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}
