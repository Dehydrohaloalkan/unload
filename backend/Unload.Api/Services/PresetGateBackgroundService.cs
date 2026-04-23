using Microsoft.AspNetCore.SignalR;
using Unload.TaskFlow;

namespace Unload.Api;

/// <summary>
/// Фоновая задача проверки доступности preset-этапа по расписанию.
/// </summary>
public  class PresetGateBackgroundService(
    PresetGateOptions options,
    IPresetGateService presetGateService,
    IWorkflowTaskAccessService workflowTaskAccessService,
    IWorkflowStageStateStore workflowStageStateStore,
    IPresetProbeService presetProbeService,
    ITaskExecutionHistoryStore taskExecutionHistoryStore,
    IHubContext<RunStatusHub> hubContext,
    ILogger<PresetGateBackgroundService> logger) : BackgroundService
{
    private readonly PresetGateOptions _options = options;
    private readonly IPresetGateService _presetGateService = presetGateService;
    private readonly IWorkflowTaskAccessService _workflowTaskAccessService = workflowTaskAccessService;
    private readonly IWorkflowStageStateStore _workflowStageStateStore = workflowStageStateStore;
    private readonly IPresetProbeService _presetProbeService = presetProbeService;
    private readonly ITaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<PresetGateBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _presetGateService.ApplyInitialOptions(_options);
        RestoreTaskStateFromHistory();
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

    private void RestoreTaskStateFromHistory()
    {
        // Эти сервисы in-memory, но факт выполнения задач за день хранится в persisted history.
        // После рестарта нужно восстановить "preset completed" и dependency-граф, иначе UI/dispatcher расходятся в состоянии.
        try
        {
            _workflowTaskAccessService.ResetCompletedTasks();
            _workflowStageStateStore.Reset();

            var today = DateOnly.FromDateTime(DateTime.Now);

            if (_taskExecutionHistoryStore.HasRunToday(WorkflowTaskCodes.Preset, today))
            {
                _workflowTaskAccessService.MarkCompleted(WorkflowTaskCodes.Preset);
                _presetGateService.MarkPresetCompleted();
            }

            if (_taskExecutionHistoryStore.HasRunToday(WorkflowTaskCodes.Extra, today))
            {
                _workflowTaskAccessService.MarkCompleted(WorkflowTaskCodes.Extra);
            }

            if (_taskExecutionHistoryStore.HasRunToday(WorkflowTaskCodes.Run, today))
            {
                _workflowTaskAccessService.MarkCompleted(WorkflowTaskCodes.Run);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore task state from history on startup.");
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (_presetGateService.RefreshDailyWindowState())
        {
            RestoreTaskStateFromHistory();
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
