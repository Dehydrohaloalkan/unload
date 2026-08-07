using Microsoft.AspNetCore.SignalR;
using Unload.Tasks;

namespace Unload.Api.Services;

/// <summary>
/// Фоновая задача проверки доступности preset-этапа по расписанию.
/// </summary>
public class ProbeSchedulerHostedService(
    PresetGateOptions options,
    DailyWindowPolicy dailyWindowPolicy,
    PresetCompletionRecovery presetCompletionRecovery,
    TaskWorkflow taskWorkflow,
    IHubContext<RunStatusHub> hubContext,
    ILogger<ProbeSchedulerHostedService> logger,
    TimeProvider timeProvider) : BackgroundService
{
    private const int MinPollIntervalSeconds = 5;

    private readonly PresetGateOptions _options = options;
    private readonly DailyWindowPolicy _dailyWindowPolicy = dailyWindowPolicy;
    private readonly PresetCompletionRecovery _presetCompletionRecovery = presetCompletionRecovery;
    private readonly TaskWorkflow _taskWorkflow = taskWorkflow;
    private readonly IHubContext<RunStatusHub> _hubContext = hubContext;
    private readonly ILogger<ProbeSchedulerHostedService> _logger = logger;
    private readonly TimeProvider _timeProvider = timeProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _dailyWindowPolicy.ApplyInitialOptions(_options);

        // Состояние DailyWindowPolicy живёт только в памяти и сбрасывается при рестарте.
        // Если preset уже выполнен сегодня (есть в истории) — восстанавливаем PresetCompleted,
        // иначе IsOpen() блокировал бы run/extra и пользователю пришлось бы запускать preset повторно.
        if (_presetCompletionRecovery.RestoreIfCompletedToday())
        {
            _logger.LogInformation("Preset completion restored from history after restart.");
        }

        _logger.LogInformation(
            "Probe scheduler initialized. Enabled: {Enabled}, Start: {StartHour:D2}:{StartMinute:D2}, PollIntervalSeconds: {PollIntervalSeconds}",
            _options.Enabled,
            Clamp(_options.StartHour, 0, 23),
            Clamp(_options.StartMinute, 0, 59),
            Math.Max(MinPollIntervalSeconds, _options.PollIntervalSeconds));
        await PublishStateAsync(stoppingToken);

        var pollIntervalSeconds = Math.Max(MinPollIntervalSeconds, _options.PollIntervalSeconds);
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollIntervalSeconds));
        try
        {
            await RunCheckSafelyAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunCheckSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Probe scheduler stopping.");
        }
        finally
        {
            timer.Dispose();
        }
    }

    /// <summary>
    /// Выполняет одну итерацию проверки, изолируя нефатальные сбои:
    /// одна неудачная итерация (например, ошибка SignalR-рассылки) не должна
    /// останавливать весь шедулер навсегда.
    /// </summary>
    private async Task RunCheckSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CheckAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Probe scheduler iteration failed; scheduler continues.");
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (_dailyWindowPolicy.RefreshDailyWindowState())
        {
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

        var now = _timeProvider.GetLocalNow().DateTime;
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
            await _taskWorkflow.LaunchAsync(new TaskLaunchRequest(TaskCode: TaskCodes.Probe), cancellationToken);
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
