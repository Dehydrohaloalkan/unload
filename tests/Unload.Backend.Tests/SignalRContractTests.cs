using Unload.Api;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Backend.Tests;

public class SignalRContractTests
{
    [Fact]
    public void Public_names_remain_compatible_with_the_frontend()
    {
        Assert.Equal("/hubs/status", RunStatusHubContract.HubPath);
        Assert.Equal("SubscribeRun", RunStatusHubContract.SubscribeMethod);
        Assert.Equal("run_status", RunStatusHubContract.RunStatusEvent);
        Assert.Equal("preset_state", RunStatusHubContract.PresetStateEvent);
        Assert.Equal("preset_replayed", RunStatusHubContract.PresetReplayedEvent);
    }

    [Fact]
    public void Typed_publishers_bind_each_event_to_its_payload()
    {
        AssertPayloadType(nameof(RunStatusHubContract.SendRunStatusAsync), typeof(RunStatusInfo));
        AssertPayloadType(nameof(RunStatusHubContract.SendPresetStateAsync), typeof(PresetGateState));
        AssertPayloadType(nameof(RunStatusHubContract.SendPresetReplayedAsync), typeof(ScriptTaskRunResult));
    }

    private static void AssertPayloadType(string methodName, Type expectedType)
    {
        var method = typeof(RunStatusHubContract).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.Equal(expectedType, method.GetParameters()[1].ParameterType);
    }
}
