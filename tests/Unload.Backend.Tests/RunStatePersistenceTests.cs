using Unload.Store;

namespace Unload.Backend.Tests;

public class RunStatePersistenceTests
{
    [Fact]
    public async Task Save_SerializesSnapshotCaptureAndWrite()
    {
        var scratchDirectory = Path.Combine(Path.GetTempPath(), $"unload-persistence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratchDirectory);
        try
        {
            var persistence = new RunStatePersistence(Path.Combine(scratchDirectory, "runs.json"));
            var firstCaptureEntered = CompletionSource();
            var releaseFirstCapture = CompletionSource();
            var secondSaveAttempted = CompletionSource();
            var secondCaptureEntered = CompletionSource();

            var firstSave = Task.Run(() => persistence.Save(() =>
            {
                firstCaptureEntered.TrySetResult(true);
                releaseFirstCapture.Task.GetAwaiter().GetResult();
                return [Run("first")];
            }));
            await firstCaptureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var secondSave = Task.Run(() =>
            {
                secondSaveAttempted.TrySetResult(true);
                persistence.Save(() =>
                {
                    secondCaptureEntered.TrySetResult(true);
                    return [Run("second")];
                });
            });
            await secondSaveAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            try
            {
                var completed = await Task.WhenAny(
                    secondCaptureEntered.Task,
                    Task.Delay(TimeSpan.FromMilliseconds(250)));
                Assert.NotSame(secondCaptureEntered.Task, completed);
            }
            finally
            {
                releaseFirstCapture.TrySetResult(true);
            }

            await Task.WhenAll(firstSave, secondSave).WaitAsync(TimeSpan.FromSeconds(5));

            var snapshot = Assert.IsType<RunStatePersistenceSnapshot>(persistence.Load());
            Assert.Equal("second", Assert.Single(snapshot.Runs).CorrelationId);
        }
        finally
        {
            Directory.Delete(scratchDirectory, recursive: true);
        }
    }

    private static TaskCompletionSource<bool> CompletionSource()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static RunStatusInfo Run(string correlationId)
    {
        var now = new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
        return new RunStatusInfo(
            correlationId,
            "run",
            RunLifecycleStatus.Running,
            TargetCodes: [],
            CreatedAt: now,
            UpdatedAt: now);
    }
}
