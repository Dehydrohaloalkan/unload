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
                "batchFileCount",
                "batchFiles",
                "batchId",
                "chunkNumber",
                "correlationId",
                "estimatedBytes",
                "failure",
                "filePath",
                "memberName",
                "message",
                "occurredAt",
                "records",
                "scriptCode",
                "sequence",
                "step",
                "workerId",
                "workOrder"
            },
            propertyNames);
    }

    [Theory]
    [InlineData(RunnerStep.ChunkCreated)]
    [InlineData(RunnerStep.FileWriteStarted)]
    [InlineData(RunnerStep.FileWritten)]
    public void High_frequency_file_events_are_not_published_as_individual_status_messages(RunnerStep step)
    {
        var @event = new RunnerEvent(DateTimeOffset.UtcNow, "run-1", step, "file lifecycle");

        Assert.False(RunStatusHubContract.ShouldPublishStatusEvent(@event));
    }

    [Theory]
    [InlineData(RunnerStep.ChunkCreated)]
    [InlineData(RunnerStep.FileWriteStarted)]
    [InlineData(RunnerStep.FileWritten)]
    public void Failure_bearing_file_events_are_never_suppressed(RunnerStep step)
    {
        var @event = new RunnerEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            step,
            "file lifecycle failed",
            Failure: new RunnerFailureInfo(
                "file_write",
                "file",
                "chunk-1",
                null,
                null,
                null,
                1,
                "output/file.csv",
                null,
                "file_write_failed",
                "Unable to write file.",
                DateTimeOffset.UtcNow));

        Assert.True(RunStatusHubContract.ShouldPublishStatusEvent(@event));
    }

    [Theory]
    [InlineData(RunnerStep.RequestAccepted)]
    [InlineData(RunnerStep.TargetsResolved)]
    [InlineData(RunnerStep.ScriptDiscovered)]
    [InlineData(RunnerStep.QueryStarted)]
    [InlineData(RunnerStep.QueryCompleted)]
    [InlineData(RunnerStep.ScriptCompleted)]
    [InlineData(RunnerStep.GatewayBatchQueued)]
    [InlineData(RunnerStep.PublishedToGateway)]
    [InlineData(RunnerStep.Completed)]
    [InlineData(RunnerStep.Failed)]
    public void Meaningful_non_file_milestones_remain_status_messages(RunnerStep step)
    {
        var @event = new RunnerEvent(DateTimeOffset.UtcNow, "run-1", step, "milestone");

        Assert.True(RunStatusHubContract.ShouldPublishStatusEvent(@event));
    }

    private static void AssertPayloadType(string methodName, Type expectedType)
    {
        var method = typeof(RunStatusHubContract).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.Equal(expectedType, method.GetParameters()[1].ParameterType);
    }
}
