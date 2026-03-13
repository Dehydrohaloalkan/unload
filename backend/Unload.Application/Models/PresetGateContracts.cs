namespace Unload.Application;

/// <summary>
/// Конфигурация фоновой проверки готовности preset-задачи.
/// </summary>
public sealed record PresetGateOptions(
    bool Enabled,
    int StartHour,
    int StartMinute,
    int PollIntervalSeconds,
    string ProbeSql)
{
    public static PresetGateOptions Default { get; } = new(
        Enabled: true,
        StartHour: 15,
        StartMinute: 0,
        PollIntervalSeconds: 60,
        ProbeSql: "/* PRESET_READY_PROBE */ SELECT 0");
}

/// <summary>
/// Текущее состояние preset-гейта для UI-клиентов.
/// </summary>
public sealed record PresetGateState(
    bool Enabled,
    bool PollingStarted,
    bool RequiresPresetExecution,
    bool ReadyForPreset,
    bool PresetCompleted,
    int? LastProbeValue,
    DateTimeOffset? LastProbeAt,
    string Message);
