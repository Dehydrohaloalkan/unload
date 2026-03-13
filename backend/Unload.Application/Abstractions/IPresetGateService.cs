namespace Unload.Application;

/// <summary>
/// Контракт сервиса бизнес-правил preset-гейта.
/// </summary>
public interface IPresetGateService
{
    PresetGateState Get();

    bool ApplyInitialOptions(PresetGateOptions options);

    bool StartPolling();

    bool RefreshDailyWindowState();

    bool ApplyProbeResult(int value, DateTimeOffset checkedAt);

    bool MarkPresetCompleted();

    bool CanRunPreset(out string reason);

    bool CanRunMainAndExtra(out string reason);
}
