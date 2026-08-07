namespace Unload.Tasks;

/// <summary>
/// Policy дневного окна — единственный источник правил доступа к задачам.
/// Заменяет <c>PresetGateService</c> и <c>IPresetGateService</c> без интерфейса.
/// </summary>
public class DailyWindowPolicy
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
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

    public DailyWindowPolicy(PresetGateOptions options, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ApplyInitialOptions(options);
    }

    /// <summary>Возвращает текущее состояние дневного окна.</summary>
    public PresetGateState Get()
    {
        lock (_sync)
        {
            EnsureCurrentDayState();
            return _state;
        }
    }

    /// <summary>Применяет начальные параметры конфигурации.</summary>
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

    /// <summary>Запускает цикл опроса (polling).</summary>
    public bool StartPolling()
    {
        lock (_sync)
        {
            EnsureCurrentDayState();
            if (_state.PollingStarted)
            {
                return false;
            }

            if (_state.PresetCompleted)
            {
                var completedNext = _state with
                {
                    PollingStarted = false,
                    RequiresPresetExecution = false,
                    ReadyForPreset = false,
                    Message = "Preset task already completed for the current day."
                };
                return ReplaceIfChanged(completedNext);
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

    /// <summary>Обновляет состояние дневного окна при смене даты.</summary>
    public bool RefreshDailyWindowState()
    {
        lock (_sync)
        {
            var previous = _state;
            EnsureCurrentDayState();
            return previous != _state;
        }
    }

    /// <summary>Применяет результат probe-запроса.</summary>
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

    /// <summary>Фиксирует успешное завершение preset на текущий день.</summary>
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
            _presetCompletedOnDate = GetCurrentDate();
            return ReplaceIfChanged(next);
        }
    }

    /// <summary>
    /// Проверяет, можно ли запустить preset прямо сейчас.
    /// <para>Условия: probe пройден + время в пределах дневного окна.</para>
    /// </summary>
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

            if (!IsWithinDailyWindow(GetCurrentTime()))
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

    /// <summary>
    /// Проверяет, открыто ли дневное окно для main-выгрузки и extra
    /// (preset выполнен + текущее время в пределах окна).
    /// </summary>
    public bool IsOpen(DateTime now)
    {
        lock (_sync)
        {
            EnsureCurrentDayState();

            if (!_state.Enabled)
            {
                return true;
            }

            var timeNow = TimeOnly.FromDateTime(now);
            if (!IsWithinDailyWindow(timeNow))
            {
                return false;
            }

            return _state.PresetCompleted;
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

        var today = GetCurrentDate();
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

    private DateOnly GetCurrentDate()
    {
        return DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
    }

    private TimeOnly GetCurrentTime()
    {
        return TimeOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
    }
}
