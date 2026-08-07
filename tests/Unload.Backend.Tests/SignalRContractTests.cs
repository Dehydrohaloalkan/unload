using System.Text.Json;
using Unload.Api;
using Unload.Core;
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
        Assert.Equal("status", RunStatusHubContract.StatusEvent);
        Assert.Equal("run_status", RunStatusHubContract.RunStatusEvent);
        Assert.Equal("preset_state", RunStatusHubContract.PresetStateEvent);
        Assert.Equal("preset_replayed", RunStatusHubContract.PresetReplayedEvent);
    }

    [Fact]
    public void Typed_publishers_bind_each_event_to_its_payload()
    {
        AssertPayloadType(nameof(RunStatusHubContract.SendStatusAsync), typeof(RunnerEvent));
        AssertPayloadType(nameof(RunStatusHubContract.SendRunStatusAsync), typeof(RunStatusInfo));
        AssertPayloadType(nameof(RunStatusHubContract.SendPresetStateAsync), typeof(PresetGateState));
        AssertPayloadType(nameof(RunStatusHubContract.SendPresetReplayedAsync), typeof(ScriptTaskRunResult));
    }

    [Fact]
    public void Runner_event_json_shape_remains_compatible_with_the_frontend()
    {
        var propertyNames = typeof(RunnerEvent)
            .GetProperties()
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .Order()
            .ToArray();

        Assert.Equal(
            new[]
            {
                "correlationId",
                "filePath",
                "memberName",
                "message",
                "occurredAt",
                "records",
                "scriptCode",
                "step",
                "workerId"
            },
            propertyNames);
    }

    private static void AssertPayloadType(string methodName, Type expectedType)
    {
        var method = typeof(RunStatusHubContract).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.Equal(expectedType, method.GetParameters()[1].ParameterType);
    }
}
