using Microsoft.AspNetCore.SignalR;
using Unload.Api.Abstractions;
using Unload.Tasks;

namespace Unload.Api.Services;

/// <summary>
/// Фоновая задача проверки доступности preset-этапа по расписанию.
/// </summary>
public class PresetGateBackgroundService(
    PresetGateOptions options,
    DailyWindowPolicy dailyWindowPolicy,
    IPresetProbeService presetProbeService,
    IWorkflowInMemoryStateRestorer workflowInMemoryStateRestorer,
    IHubContext<RunStatusHub> hubContext,
    ILogger<PresetGateBackgroundService> logger) : BackgroundService
{
    private readonly PresetGateOptions _options = options;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly IPresetProbeService _presetProbeService = presetProbeService;
    private readonly IWorkflowInMemoryStateRestorer _workflowInMemoryStateRestorer = workflowInMemoryStateRestorer;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<PresetGateBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _dailyWindowPolicy.ApplyInitialOptions(_options);
        _workflowInMemoryStateRestorer.RestoreForToday();
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
        if (_dailyWindowPolicy.RefreshDailyWindowState())
        {
            _workflowInMemoryStateRestorer.RestoreForToday();
            _logger.LogInformation("Preset gate daily window state updated.");
            await PublishStateAsync(cancellationToken);
        }

        if (!_options.Enabled)
        {
            return;
        }

        // If preset is already completed for the current day, do not start polling/probing.
        var currentState = _dailyWindowPolicy.Get();
        if (currentState.PresetCompleted)
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

        if (_dailyWindowPolicy.StartPolling())
        {
            _logger.LogInformation("Preset gate polling started.");
            await PublishStateAsync(cancellationToken);
        }

        var state = _dailyWindowPolicy.Get();
        if (state.PresetCompleted || state.ReadyForPreset)
        {
            return;
        }

        try
        {
            var previous = _dailyWindowPolicy.Get();
            await _presetProbeService.ExecuteAndApplyAsync(cancellationToken);
            var current = _dailyWindowPolicy.Get();

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
        await _hubContext.Clients.All.SendAsync("preset_state", _dailyWindowPolicy.Get(), cancellationToken);
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}
