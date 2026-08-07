using Microsoft.Extensions.Logging.Abstractions;
using Unload.Core;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Backend.Tests;

public class TaskWorkflowTests
{
    private static readonly DateTimeOffset Today = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UnknownTask_ReturnsValidationErrorWithoutExecutingRegisteredTasks()
    {
        using var fixture = new WorkflowFixture(Today);
        var registered = TestTask.Completed("known");
        var workflow = fixture.CreateWorkflow(registered);

        var error = await Assert.ThrowsAsync<TaskLaunchException>(
            () => workflow.LaunchAsync(new TaskLaunchRequest("missing"), CancellationToken.None));

        Assert.Equal(TaskLaunchFailureKind.Validation, error.FailureKind);
        Assert.Equal("VALIDATION_ERROR", error.ErrorCode);
        Assert.Equal(0, registered.ExecuteCount);
    }

    [Fact]
    public async Task ClosedDailyWindow_BlocksMainAndExtraTasks()
    {
        using var fixture = new WorkflowFixture(Today);
        var main = TestTask.Completed(TaskCodes.Run, requiresDailyWindowOpen: true, isDeferred: true);
        var extra = TestTask.Completed(TaskCodes.Extra, requiresDailyWindowOpen: true, isDeferred: true);
        var workflow = fixture.CreateWorkflow(main, extra);

        foreach (var taskCode in new[] { TaskCodes.Run, TaskCodes.Extra })
        {
            var error = await Assert.ThrowsAsync<TaskLaunchException>(
                () => workflow.LaunchAsync(new TaskLaunchRequest(taskCode), CancellationToken.None));

            Assert.Equal("PRESET_GATE_BLOCKED", error.ErrorCode);
        }

        Assert.Equal(0, main.ExecuteCount);
        Assert.Equal(0, extra.ExecuteCount);
    }

    [Fact]
    public async Task ClosedPresetWindow_ReturnsReasonFromDailyWindowPolicy()
    {
        using var fixture = new WorkflowFixture(Today);
        var preset = TestTask.Completed(TaskCodes.Preset, requiresPresetWindow: true);
        var workflow = fixture.CreateWorkflow(preset);

        var error = await Assert.ThrowsAsync<TaskLaunchException>(
            () => workflow.LaunchAsync(new TaskLaunchRequest(TaskCodes.Preset), CancellationToken.None));

        Assert.Equal("PRESET_GATE_BLOCKED", error.ErrorCode);
        Assert.Equal("Preset gate has not started yet.", error.Message);
        Assert.Equal(0, preset.ExecuteCount);
    }

    [Fact]
    public async Task MissingDependencies_AreReturnedInRequiredTaskCodes()
    {
        using var fixture = new WorkflowFixture(Today);
        var task = TestTask.Completed("dependent", requiresCompleted: ["probe", "preset"]);
        var workflow = fixture.CreateWorkflow(task);

        var error = await Assert.ThrowsAsync<TaskLaunchException>(
            () => workflow.LaunchAsync(new TaskLaunchRequest(task.Code), CancellationToken.None));

        Assert.Equal("TASK_DEPENDENCY_NOT_SATISFIED", error.ErrorCode);
        var requiredTaskCodes = Assert.IsType<string[]>(error.Extensions!["requiredTaskCodes"]);
        Assert.Equal(["probe", "preset"], requiredTaskCodes);
    }

    [Fact]
    public async Task Dependencies_UseCurrentLocalDateFromTimeProvider()
    {
        using var fixture = new WorkflowFixture(Today);
        fixture.History.Add("probe", Today.AddDays(-1), Today.AddDays(-1), null, null);
        var task = TestTask.Completed("dependent", requiresCompleted: ["probe"]);
        var workflow = fixture.CreateWorkflow(task);

        await Assert.ThrowsAsync<TaskLaunchException>(
            () => workflow.LaunchAsync(new TaskLaunchRequest(task.Code), CancellationToken.None));

        fixture.History.Add("probe", Today, Today, null, null);
        var result = await workflow.LaunchAsync(
            new TaskLaunchRequest(task.Code),
            CancellationToken.None);

        Assert.Equal(TaskExecutionStatus.Completed, result.Status);
        Assert.Equal(1, task.ExecuteCount);
    }

    [Fact]
    public async Task ActiveRun_BlocksSecondRunAndTaskConflictingWithRun()
    {
        using var fixture = new WorkflowFixture(Today);
        fixture.Window.MarkPresetCompleted();
        Assert.True(fixture.RunChannel.TryActivate(
            "active-run",
            new RunRequest([], "active-run", fixture.ScratchDirectory)));
        var run = TestTask.Completed(TaskCodes.Run, isDeferred: true);
        var preset = TestTask.Completed(TaskCodes.Preset, conflictsWith: [TaskCodes.Run]);
        var workflow = fixture.CreateWorkflow(run, preset);

        foreach (var taskCode in new[] { TaskCodes.Run, TaskCodes.Preset })
        {
            var error = await Assert.ThrowsAsync<TaskLaunchException>(
                () => workflow.LaunchAsync(new TaskLaunchRequest(taskCode), CancellationToken.None));

            Assert.Equal("RUN_ALREADY_IN_PROGRESS", error.ErrorCode);
            Assert.Equal("active-run", error.Extensions!["activeCorrelationId"]);
        }
    }

    [Fact]
    public async Task ActiveExtra_BlocksSecondExtraAndTaskConflictingWithExtra()
    {
        using var fixture = new WorkflowFixture(Today);
        Assert.True(fixture.ExtraChannel.TryActivate(
            "active-extra",
            new ExtraRunRequest("active-extra", [], null, true)));
        var extra = TestTask.Completed(TaskCodes.Extra, isDeferred: true);
        var preset = TestTask.Completed(TaskCodes.Preset, conflictsWith: [TaskCodes.Extra]);
        var workflow = fixture.CreateWorkflow(extra, preset);

        foreach (var taskCode in new[] { TaskCodes.Extra, TaskCodes.Preset })
        {
            var error = await Assert.ThrowsAsync<TaskLaunchException>(
                () => workflow.LaunchAsync(new TaskLaunchRequest(taskCode), CancellationToken.None));

            Assert.Equal("TASK_ALREADY_RUNNING", error.ErrorCode);
            Assert.Equal("active-extra", error.Extensions!["activeCorrelationId"]);
        }
    }

    [Fact]
    public async Task ForegroundConflict_IsCheckedSymmetrically()
    {
        using var fixture = new WorkflowFixture(Today);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = TestTask.Blocking("active", release, conflictsWith: ["candidate"]);
        var candidate = TestTask.Completed("candidate");
        var workflow = fixture.CreateWorkflow(active, candidate);
        var activeLaunch = workflow.LaunchAsync(
            new TaskLaunchRequest(active.Code),
            CancellationToken.None);
        await active.Started;

        var error = await Assert.ThrowsAsync<TaskLaunchException>(
            () => workflow.LaunchAsync(new TaskLaunchRequest(candidate.Code), CancellationToken.None));

        Assert.Equal("TASK_ALREADY_RUNNING", error.ErrorCode);
        release.SetResult();
        await activeLaunch;
    }

    [Fact]
    public async Task ConcurrentConflictingLaunches_ExecuteOnlyOneTask()
    {
        using var fixture = new WorkflowFixture(Today);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = TestTask.Blocking("first", release, conflictsWith: ["second"]);
        var second = TestTask.Blocking("second", release, conflictsWith: ["first"]);
        var workflow = fixture.CreateWorkflow(first, second);

        var launches = new[]
        {
            CaptureLaunch(workflow, first.Code),
            CaptureLaunch(workflow, second.Code)
        };
        await Task.WhenAny(first.Started, second.Started);
        await WaitUntilAsync(() => launches.Count(static launch => launch.IsCompleted) >= 1);

        Assert.Equal(1, first.ExecuteCount + second.ExecuteCount);
        release.SetResult();
        var outcomes = await Task.WhenAll(launches);
        Assert.Single(outcomes, static outcome => outcome.Result is not null);
        Assert.Single(outcomes, static outcome => outcome.Error?.ErrorCode == "TASK_ALREADY_RUNNING");
    }

    [Theory]
    [InlineData(TaskOutcome.Success)]
    [InlineData(TaskOutcome.Exception)]
    [InlineData(TaskOutcome.Cancellation)]
    public async Task ForegroundSlot_IsReleasedAfterTerminalOutcome(TaskOutcome outcome)
    {
        using var fixture = new WorkflowFixture(Today);
        var first = TestTask.WithOutcome("first", outcome, conflictsWith: ["second"]);
        var second = TestTask.Completed("second");
        var workflow = fixture.CreateWorkflow(first, second);

        if (outcome == TaskOutcome.Success)
        {
            await workflow.LaunchAsync(new TaskLaunchRequest(first.Code), CancellationToken.None);
        }
        else if (outcome == TaskOutcome.Exception)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => workflow.LaunchAsync(new TaskLaunchRequest(first.Code), CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => workflow.LaunchAsync(new TaskLaunchRequest(first.Code), CancellationToken.None));
        }

        var result = await workflow.LaunchAsync(
            new TaskLaunchRequest(second.Code),
            CancellationToken.None);
        Assert.Equal(TaskExecutionStatus.Completed, result.Status);
    }

    [Fact]
    public async Task DeferredTask_DoesNotHoldForegroundSlot()
    {
        using var fixture = new WorkflowFixture(Today);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deferred = TestTask.Blocking("deferred", release, conflictsWith: ["foreground"], isDeferred: true);
        var foreground = TestTask.Completed("foreground", conflictsWith: ["deferred"]);
        var workflow = fixture.CreateWorkflow(deferred, foreground);
        var deferredLaunch = workflow.LaunchAsync(
            new TaskLaunchRequest(deferred.Code),
            CancellationToken.None);
        await deferred.Started;

        var foregroundResult = await workflow.LaunchAsync(
            new TaskLaunchRequest(foreground.Code),
            CancellationToken.None);

        Assert.Equal(TaskExecutionStatus.Completed, foregroundResult.Status);
        release.SetResult();
        await deferredLaunch;
    }

    [Fact]
    public async Task AdminOverride_BypassesGateDependenciesAndActiveRunCheck()
    {
        using var fixture = new WorkflowFixture(Today);
        Assert.True(fixture.RunChannel.TryActivate(
            "active-run",
            new RunRequest([], "active-run", fixture.ScratchDirectory)));
        var run = TestTask.Completed(
            TaskCodes.Run,
            requiresCompleted: ["missing"],
            requiresDailyWindowOpen: true,
            isDeferred: true);
        var workflow = fixture.CreateWorkflow(run);

        var result = await workflow.LaunchAsync(
            new TaskLaunchRequest(TaskCodes.Run, AdminOverride: true),
            CancellationToken.None);

        Assert.Equal(TaskExecutionStatus.Completed, result.Status);
        Assert.Equal(1, run.ExecuteCount);
    }

    [Fact]
    public async Task TaskCodesAndConflicts_AreCaseInsensitive()
    {
        using var fixture = new WorkflowFixture(Today);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = TestTask.Blocking("active", release);
        var candidate = TestTask.Completed("candidate", conflictsWith: ["ACTIVE"]);
        var workflow = fixture.CreateWorkflow(active, candidate);
        var activeLaunch = workflow.LaunchAsync(
            new TaskLaunchRequest("ACTIVE"),
            CancellationToken.None);
        await active.Started;

        var error = await Assert.ThrowsAsync<TaskLaunchException>(
            () => workflow.LaunchAsync(new TaskLaunchRequest("CANDIDATE"), CancellationToken.None));

        Assert.Equal("TASK_ALREADY_RUNNING", error.ErrorCode);
        release.SetResult();
        await activeLaunch;
    }

    [Fact]
    public async Task ExecuteAsync_ReceivesOriginalRequestAndCancellationTokenAndReturnsOriginalResult()
    {
        using var fixture = new WorkflowFixture(Today);
        var expectedResult = new TaskExecutionResult(
            "forwarded", "execution-1", TaskExecutionStatus.Accepted, "accepted", 2, 3, fixture.ScratchDirectory);
        TaskLaunchRequest? receivedRequest = null;
        CancellationToken receivedToken = default;
        var task = new TestTask("forwarded", (request, token) =>
        {
            receivedRequest = request;
            receivedToken = token;
            return Task.FromResult(expectedResult);
        });
        var workflow = fixture.CreateWorkflow(task);
        var request = new TaskLaunchRequest(
            "FORWARDED", PublishToGateway: false, Codes: ["A"], SelectionMode: RunSelectionMode.TargetCodes);
        using var cts = new CancellationTokenSource();

        var actualResult = await workflow.LaunchAsync(request, cts.Token);

        Assert.Same(request, receivedRequest);
        Assert.Equal(cts.Token, receivedToken);
        Assert.Same(expectedResult, actualResult);
    }

    private static async Task<LaunchOutcome> CaptureLaunch(TaskWorkflow workflow, string taskCode)
    {
        try
        {
            var result = await workflow.LaunchAsync(
                new TaskLaunchRequest(taskCode),
                CancellationToken.None);
            return new LaunchOutcome(result, null);
        }
        catch (TaskLaunchException error)
        {
            return new LaunchOutcome(null, error);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    public enum TaskOutcome
    {
        Success,
        Exception,
        Cancellation
    }

    private sealed record LaunchOutcome(TaskExecutionResult? Result, TaskLaunchException? Error);

    private sealed class WorkflowFixture : IDisposable
    {
        private readonly ManualTimeProvider _timeProvider;

        public WorkflowFixture(DateTimeOffset now)
        {
            _timeProvider = new ManualTimeProvider(now);
            ScratchDirectory = Path.Combine(Path.GetTempPath(), $"unload-task-workflow-{Guid.NewGuid():N}");
            Directory.CreateDirectory(ScratchDirectory);
            History = new TaskExecutionHistoryStore(Path.Combine(ScratchDirectory, "history.json"));
            Window = new DailyWindowPolicy(
                new PresetGateOptions(true, 9, 0, 60, "SELECT 0"),
                _timeProvider);
        }

        public string ScratchDirectory { get; }

        public TaskExecutionHistoryStore History { get; }

        public DailyWindowPolicy Window { get; }

        public RunActivationChannel RunChannel { get; } = new();

        public ExtraActivationChannel ExtraChannel { get; } = new();

        public TaskWorkflow CreateWorkflow(params TestTask[] tasks)
        {
            return new TaskWorkflow(
                tasks,
                Window,
                History,
                RunChannel,
                ExtraChannel,
                NullLogger<TaskWorkflow>.Instance,
                _timeProvider);
        }

        public void Dispose()
        {
            Directory.Delete(ScratchDirectory, recursive: true);
        }
    }

    private sealed class TestTask : UnloadTask
    {
        private readonly Func<TaskLaunchRequest, CancellationToken, Task<TaskExecutionResult>> _execute;

        public TestTask(
            string code,
            Func<TaskLaunchRequest, CancellationToken, Task<TaskExecutionResult>> execute,
            IReadOnlyCollection<string>? requiresCompleted = null,
            IReadOnlyCollection<string>? conflictsWith = null,
            bool requiresDailyWindowOpen = false,
            bool requiresPresetWindow = false,
            bool isDeferred = false)
        {
            Code = code;
            _execute = execute;
            RequiresCompleted = requiresCompleted ?? [];
            ConflictsWith = conflictsWith ?? [];
            RequiresDailyWindowOpen = requiresDailyWindowOpen;
            RequiresPresetWindow = requiresPresetWindow;
            IsDeferred = isDeferred;
        }

        public override string Code { get; }

        public override IReadOnlyCollection<string> RequiresCompleted { get; }

        public override IReadOnlyCollection<string> ConflictsWith { get; }

        public override bool RequiresDailyWindowOpen { get; }

        public override bool RequiresPresetWindow { get; }

        public override bool IsDeferred { get; }

        public int ExecuteCount { get; private set; }

        public Task Started { get; private set; } = Task.CompletedTask;

        public override Task<TaskExecutionResult> ExecuteAsync(TaskLaunchRequest request, CancellationToken cancellationToken)
        {
            ExecuteCount++;
            return _execute(request, cancellationToken);
        }

        public static TestTask Completed(
            string code,
            IReadOnlyCollection<string>? requiresCompleted = null,
            IReadOnlyCollection<string>? conflictsWith = null,
            bool requiresDailyWindowOpen = false,
            bool requiresPresetWindow = false,
            bool isDeferred = false)
        {
            return new TestTask(
                code,
                (_, _) => Task.FromResult(
                    new TaskExecutionResult(code, $"{code}-execution", TaskExecutionStatus.Completed, "completed")),
                requiresCompleted,
                conflictsWith,
                requiresDailyWindowOpen,
                requiresPresetWindow,
                isDeferred);
        }

        public static TestTask Blocking(
            string code,
            TaskCompletionSource release,
            IReadOnlyCollection<string>? conflictsWith = null,
            bool isDeferred = false)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = new TestTask(
                code,
                async (_, _) =>
                {
                    started.SetResult();
                    await release.Task;
                    return new TaskExecutionResult(code, $"{code}-execution", TaskExecutionStatus.Completed, "completed");
                },
                conflictsWith: conflictsWith,
                isDeferred: isDeferred)
            {
                Started = started.Task
            };
            return task;
        }

        public static TestTask WithOutcome(
            string code,
            TaskOutcome outcome,
            IReadOnlyCollection<string>? conflictsWith = null)
        {
            return new TestTask(
                code,
                (_, _) => outcome switch
                {
                    TaskOutcome.Success => Task.FromResult(
                        new TaskExecutionResult(code, $"{code}-execution", TaskExecutionStatus.Completed, "completed")),
                    TaskOutcome.Exception => Task.FromException<TaskExecutionResult>(new InvalidOperationException("failed")),
                    TaskOutcome.Cancellation => Task.FromCanceled<TaskExecutionResult>(new CancellationToken(canceled: true)),
                    _ => throw new ArgumentOutOfRangeException(nameof(outcome))
                },
                conflictsWith: conflictsWith);
        }
    }
}
