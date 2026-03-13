using Microsoft.AspNetCore.SignalR;
using Unload.Core;

namespace Unload.Api;

/// <summary>
/// Фоновая задача проверки доступности preset-этапа по расписанию.
/// </summary>
public sealed class PresetGateBackgroundService : BackgroundService
{
    private readonly PresetGateOptions _options;
    private readonly PresetGateStateStore _stateStore;
    private readonly IDatabaseClientFactory _databaseClientFactory;
    private readonly IHubContext<RunStatusHub> _hubContext;
    private readonly ILogger<PresetGateBackgroundService> _logger;

    public PresetGateBackgroundService(
        PresetGateOptions options,
        PresetGateStateStore stateStore,
        IDatabaseClientFactory databaseClientFactory,
        IHubContext<RunStatusHub> hubContext,
        ILogger<PresetGateBackgroundService> logger)
    {
        _options = options;
        _stateStore = stateStore;
        _databaseClientFactory = databaseClientFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stateStore.ApplyInitialOptions(_options);
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
        if (_stateStore.RefreshDailyWindowState())
        {
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

        if (_stateStore.StartPolling())
        {
            _logger.LogInformation("Preset gate polling started.");
            await PublishStateAsync(cancellationToken);
        }

        var state = _stateStore.Get();
        if (state.PresetCompleted || state.ReadyForPreset)
        {
            return;
        }

        try
        {
            var probeResult = await ProbeAsync(cancellationToken);
            if (_stateStore.ApplyProbeResult(probeResult, DateTimeOffset.UtcNow))
            {
                _logger.LogInformation("Preset gate probe state changed. ProbeResult: {ProbeResult}", probeResult);
                await PublishStateAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preset probe failed.");
        }
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

    private async Task PublishStateAsync(CancellationToken cancellationToken)
    {
        await _hubContext.Clients.All.SendAsync("preset_state", _stateStore.Get(), cancellationToken);
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}
