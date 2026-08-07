using Microsoft.AspNetCore.Mvc;
using Unload.Api.Controllers;
using Unload.Core;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Backend.Tests;

public class RunStatusControllerTests : IDisposable
{
    private readonly string _scratchDirectory = Path.Combine(
        Path.GetTempPath(),
        $"unload-run-status-{Guid.NewGuid():N}");

    [Fact]
    public void GetActiveRun_WithoutActiveRunReturnsNotFound()
    {
        var controller = CreateController(new RunActivationChannel(), CreateStore());

        Assert.IsType<NotFoundResult>(controller.GetActiveRun().Result);
    }

    [Fact]
    public void GetActiveRun_WithPersistedActiveRunReturnsFullStatus()
    {
        var channel = new RunActivationChannel();
        var store = CreateStore();
        const string correlationId = "run-contract";
        Assert.True(channel.TryActivate(
            correlationId,
            new RunRequest(["TARGET"], correlationId, _scratchDirectory, PublishToGateway: false)));
        store.SetStarted(correlationId, ["TARGET"], ["MEMBER"], publishToGateway: false);
        var controller = CreateController(channel, store);

        var result = Assert.IsType<OkObjectResult>(controller.GetActiveRun().Result);
        var status = Assert.IsType<RunStatusInfo>(result.Value);

        Assert.Equal(correlationId, status.CorrelationId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDirectory))
        {
            Directory.Delete(_scratchDirectory, recursive: true);
        }
    }

    private RunStateStore CreateStore()
    {
        Directory.CreateDirectory(_scratchDirectory);
        return new RunStateStore(1, Path.Combine(_scratchDirectory, "runs.json"));
    }

    private static RunStatusController CreateController(
        RunActivationChannel channel,
        RunStateStore store) =>
        new(channel, store);
}
