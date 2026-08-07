using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Unload.Api.Controllers;
using Unload.Api.Models;
using Unload.Store;

namespace Unload.Backend.Tests;

public class SystemControllerHealthTests
{
    [Fact]
    public void GetHealth_HealthyStoresReturnsOk()
    {
        using var paths = new HealthPaths();
        var controller = CreateController(
            new RunStateStore(workerCount: 1, paths.RunStateFilePath),
            new TaskExecutionHistoryStore(paths.TaskHistoryFilePath));

        var result = Assert.IsType<OkObjectResult>(controller.GetHealth().Result);
        var response = Assert.IsType<SystemHealthResponse>(result.Value);

        Assert.Equal("healthy", response.Status);
        Assert.True(response.RunState.IsWritable);
        Assert.True(response.TaskHistory.IsWritable);
    }

    [Fact]
    public void GetHealth_DegradedStoreReturnsServiceUnavailableWithoutInternalPath()
    {
        using var paths = new HealthPaths(blockRunStateDirectory: true);
        var runStateStore = new RunStateStore(workerCount: 1, paths.RunStateFilePath);
        Assert.Throws<IOException>(() => runStateStore.SetRunning("run-1"));
        var controller = CreateController(
            runStateStore,
            new TaskExecutionHistoryStore(paths.TaskHistoryFilePath));

        var result = Assert.IsType<ObjectResult>(controller.GetHealth().Result);
        var response = Assert.IsType<SystemHealthResponse>(result.Value);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("degraded", response.Status);
        Assert.Equal("degraded", response.RunState.Status);
        Assert.False(response.RunState.IsWritable);
        Assert.DoesNotContain(paths.ScratchDirectory, JsonSerializer.Serialize(response));
    }

    private static SystemController CreateController(
        RunStateStore runStateStore,
        TaskExecutionHistoryStore taskHistoryStore)
    {
        return new SystemController(
            gatewayUploadService: null!,
            outputFilesService: null!,
            gatewaySenderFeedbackConsumer: null!,
            runStateStore,
            taskHistoryStore,
            NullLogger<SystemController>.Instance);
    }

    private sealed class HealthPaths : IDisposable
    {
        public HealthPaths(bool blockRunStateDirectory = false)
        {
            ScratchDirectory = Path.Combine(Path.GetTempPath(), $"unload-health-{Guid.NewGuid():N}");
            Directory.CreateDirectory(ScratchDirectory);
            if (blockRunStateDirectory)
            {
                var blockedDirectory = Path.Combine(ScratchDirectory, "blocked");
                File.WriteAllText(blockedDirectory, "this path is a file");
                RunStateFilePath = Path.Combine(blockedDirectory, "runs.json");
            }
            else
            {
                RunStateFilePath = Path.Combine(ScratchDirectory, "runs.json");
            }

            TaskHistoryFilePath = Path.Combine(ScratchDirectory, "task-history.json");
        }

        public string ScratchDirectory { get; }

        public string RunStateFilePath { get; }

        public string TaskHistoryFilePath { get; }

        public void Dispose()
        {
            Directory.Delete(ScratchDirectory, recursive: true);
        }
    }
}
