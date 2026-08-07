using Microsoft.AspNetCore.SignalR;
using Unload.Core;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Api;

/// <summary>
/// Имена и типизированные способы публикации SignalR-контракта.
/// Менять вместе с frontend contract tests.
/// </summary>
public static class RunStatusHubContract
{
    public const string HubPath = "/hubs/status";
    public const string SubscribeMethod = nameof(RunStatusHub.SubscribeRun);
    public const string StatusEvent = "status";
    public const string RunStatusEvent = "run_status";
    public const string PresetStateEvent = "preset_state";
    public const string PresetReplayedEvent = "preset_replayed";

    public static Task SendStatusAsync(
        this IClientProxy client,
        RunnerEvent payload,
        CancellationToken cancellationToken) =>
        client.SendAsync(StatusEvent, payload, cancellationToken);

    public static Task SendRunStatusAsync(
        this IClientProxy client,
        RunStatusInfo payload,
        CancellationToken cancellationToken) =>
        client.SendAsync(RunStatusEvent, payload, cancellationToken);

    public static Task SendPresetStateAsync(
        this IClientProxy client,
        PresetGateState payload,
        CancellationToken cancellationToken) =>
        client.SendAsync(PresetStateEvent, payload, cancellationToken);

    public static Task SendPresetReplayedAsync(
        this IClientProxy client,
        ScriptTaskRunResult payload,
        CancellationToken cancellationToken) =>
        client.SendAsync(PresetReplayedEvent, payload, cancellationToken);
}
