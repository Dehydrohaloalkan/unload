namespace Unload.Application;

/// <summary>
/// Потокобезопасный in-memory сервис состояния и правил preset-гейта.
/// </summary>
public sealed class PresetGateService : IPresetGateService
{
    private readonly object _sync = new();
    private TimeOnly _startTime = new(15, 0);
    private static readonly TimeOnly EndOfDayTime = new(23, 59);
    private DateOnly? _presetCompletedOnDate;
    private PresetGateState _state = new(
        Enabled: true,
        PollingStarted: false,
        RequiresPresetExecution: false,
        ReadyForPreset: false,
        PresetCompleted: false,
        LastProbeValue: null,
        LastProbeAt: null,
        Message: "Preset gate is waiting for schedule.");

    public PresetGateState Get()
    {
        lock (_sync)
        {
            return _state;
        }
    }

    public bool ApplyInitialOptions(PresetGateOptions options)
    {
        lock (_sync)
        {
            _startTime = new(
                Clamp(options.StartHour, 0, 23),
                Clamp(options.StartMinute, 0, 59));

            var enabledMessage =
                $"Main and extra tasks are locked until {_startTime:HH\\:mm}. " +
                $"Daily window: {_startTime:HH\\:mm}-23:59. Preset execution is required.";
            var next = _state with
            {
                Enabled = options.Enabled,
                PollingStarted = false,
                RequiresPresetExecution = options.Enabled,
                ReadyForPreset = false,
                PresetCompleted = false,
                LastProbeValue = null,
                LastProbeAt = null,
                Message = options.Enabled
                    ? enabledMessage
                    : "Preset gate is disabled."
            };
            _presetCompletedOnDate = null;
            return ReplaceIfChanged(next);
        }
    }

    public bool StartPolling()
    {
        lock (_sync)
        {
            if (_state.PollingStarted)
            {
                return false;
            }

            var next = _state with
            {
                PollingStarted = true,
                RequiresPresetExecution = true,
                ReadyForPreset = false,
                PresetCompleted = false,
                Message = "Preset gate started. Waiting for probe result = 1."
            };
            return ReplaceIfChanged(next);
        }
    }

    public bool RefreshDailyWindowState()
    {
        lock (_sync)
        {
            var previous = _state;
            EnsureCurrentDayState();
            return previous != _state;
        }
    }

    public bool ApplyProbeResult(int value, DateTimeOffset checkedAt)
    {
        lock (_sync)
        {
            var isReady = value == 1;
            var next = _state with
            {
                ReadyForPreset = isReady,
                LastProbeValue = value,
                LastProbeAt = checkedAt,
                Message = isReady
                    ? "Preset is ready to run. Probe monitoring is completed."
                    : "Preset is not ready yet."
            };
            return ReplaceIfChanged(next);
        }
    }

    public bool MarkPresetCompleted()
    {
        lock (_sync)
        {
            var next = _state with
            {
                PresetCompleted = true,
                ReadyForPreset = false,
                RequiresPresetExecution = false,
                Message = "Preset task completed. Main and extra tasks are unlocked until 23:59."
            };
            _presetCompletedOnDate = DateOnly.FromDateTime(DateTime.Now);
            return ReplaceIfChanged(next);
        }
    }

    public bool CanRunPreset(out string reason)
    {
        lock (_sync)
        {
            EnsureCurrentDayState();

            if (!_state.Enabled)
            {
                reason = "Preset gate is disabled.";
                return false;
            }

            if (!_state.PollingStarted)
            {
                reason = "Preset gate has not started yet.";
                return false;
            }

            if (!IsWithinDailyWindow(TimeOnly.FromDateTime(DateTime.Now)))
            {
                reason = $"Preset is available only from {_startTime:HH\\:mm} to 23:59.";
                return false;
            }

            if (_state.PresetCompleted)
            {
                reason = "Preset task is already completed.";
                return false;
            }

            if (!_state.ReadyForPreset)
            {
                reason = "Probe result is still 0. Preset task is not available yet.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }

    public bool CanRunMainAndExtra(out string reason)
    {
        lock (_sync)
        {
            EnsureCurrentDayState();

            if (!_state.Enabled)
            {
                reason = string.Empty;
                return true;
            }

            var now = TimeOnly.FromDateTime(DateTime.Now);
            if (!IsWithinDailyWindow(now))
            {
                if (now < _startTime)
                {
                    reason = $"Main and extra tasks are available only after {_startTime:HH\\:mm}.";
                    return false;
                }

                reason = "Main and extra tasks are available only until 23:59.";
                return false;
            }

            if (!_state.PresetCompleted)
            {
                reason = "Main and extra tasks are locked until preset task is completed.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }

    private bool IsWithinDailyWindow(TimeOnly now)
    {
        return now >= _startTime && now <= EndOfDayTime;
    }

    private void EnsureCurrentDayState()
    {
        if (!_state.Enabled || _presetCompletedOnDate is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_presetCompletedOnDate.Value == today)
        {
            return;
        }

        _presetCompletedOnDate = null;
        _state = _state with
        {
            PollingStarted = false,
            RequiresPresetExecution = true,
            ReadyForPreset = false,
            PresetCompleted = false,
            LastProbeValue = null,
            LastProbeAt = null,
            Message =
                $"Daily preset window reset. Main and extra tasks are locked until {_startTime:HH\\:mm}. " +
                "Complete preset for the current day."
        };
    }

    private bool ReplaceIfChanged(PresetGateState next)
    {
        if (next == _state)
        {
            return false;
        }

        _state = next;
        return true;
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}
