using Unload.Tasks;

namespace Unload.Backend.Tests;

public class DailyWindowPolicyTests
{
    private static readonly DateTimeOffset Morning = new(2026, 8, 7, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void DisabledGate_LeavesMainWindowOpenAndPresetUnavailable()
    {
        var policy = CreatePolicy(Morning, enabled: false);

        var canRunPreset = policy.CanRunPreset(out var reason);

        Assert.True(policy.IsOpen(Morning.DateTime));
        Assert.False(canRunPreset);
        Assert.Equal("Preset gate is disabled.", reason);
        Assert.Equal("Preset gate is disabled.", policy.Get().Message);
    }

    [Fact]
    public void InitialOptions_ClampInvalidStartTime()
    {
        var policy = CreatePolicy(Morning, startHour: 99, startMinute: -10);

        Assert.Contains("23:00", policy.Get().Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StartPolling_ChangesStateOnlyOnce()
    {
        var policy = CreatePolicy(Morning);

        Assert.True(policy.StartPolling());
        Assert.False(policy.StartPolling());

        var state = policy.Get();
        Assert.True(state.PollingStarted);
        Assert.True(state.RequiresPresetExecution);
        Assert.False(state.ReadyForPreset);
    }

    [Fact]
    public void ProbeZero_KeepsPresetUnavailable()
    {
        var policy = CreatePolicy(Morning);
        policy.StartPolling();

        policy.ApplyProbeResult(0, Morning);

        Assert.False(policy.CanRunPreset(out var reason));
        Assert.Equal("Probe result is still 0. Preset task is not available yet.", reason);
        Assert.Equal(0, policy.Get().LastProbeValue);
    }

    [Fact]
    public void ProbeOne_WithinWindow_MakesPresetAvailable()
    {
        var policy = CreatePolicy(Morning);
        policy.StartPolling();

        policy.ApplyProbeResult(1, Morning);

        Assert.True(policy.CanRunPreset(out var reason));
        Assert.Empty(reason);
        Assert.True(policy.Get().ReadyForPreset);
    }

    [Fact]
    public void PresetBeforePolling_IsRejected()
    {
        var policy = CreatePolicy(Morning);

        Assert.False(policy.CanRunPreset(out var reason));
        Assert.Equal("Preset gate has not started yet.", reason);
    }

    [Fact]
    public void PresetBeforeWindow_IsRejectedEvenAfterSuccessfulProbe()
    {
        var currentTime = new DateTimeOffset(2026, 8, 7, 9, 29, 59, TimeSpan.Zero);
        var policy = CreatePolicy(currentTime);
        policy.StartPolling();
        policy.ApplyProbeResult(1, currentTime);

        Assert.False(policy.CanRunPreset(out var reason));
        Assert.Equal("Preset is available only from 09:30 to 23:59.", reason);
    }

    [Theory]
    [InlineData(9, 30, 0, true)]
    [InlineData(23, 59, 0, true)]
    [InlineData(23, 59, 1, false)]
    public void MainWindow_UsesCurrentInclusiveBoundaries(int hour, int minute, int second, bool expectedOpen)
    {
        var currentTime = new DateTimeOffset(2026, 8, 7, hour, minute, second, TimeSpan.Zero);
        var policy = CreatePolicy(currentTime);
        policy.MarkPresetCompleted();

        Assert.Equal(expectedOpen, policy.IsOpen(currentTime.DateTime));
    }

    [Fact]
    public void CompletedPreset_UnlocksMainAndExtraForCurrentDay()
    {
        var policy = CreatePolicy(Morning);

        policy.MarkPresetCompleted();

        var state = policy.Get();
        Assert.True(policy.IsOpen(Morning.DateTime));
        Assert.True(state.PresetCompleted);
        Assert.False(state.RequiresPresetExecution);
        Assert.False(state.ReadyForPreset);
    }

    [Fact]
    public void CompletedPreset_CannotRunAgainOnSameDay()
    {
        var policy = CreatePolicy(Morning);
        policy.StartPolling();
        policy.ApplyProbeResult(1, Morning);
        policy.MarkPresetCompleted();

        Assert.False(policy.CanRunPreset(out var reason));
        Assert.Equal("Preset task is already completed.", reason);
    }

    [Fact]
    public void NextDay_ResetsCompletionProbeAndPolling()
    {
        var timeProvider = new ManualTimeProvider(Morning);
        var policy = CreatePolicy(timeProvider);
        policy.StartPolling();
        policy.ApplyProbeResult(1, Morning);
        policy.MarkPresetCompleted();

        timeProvider.SetUtcNow(Morning.AddDays(1));
        var changed = policy.RefreshDailyWindowState();

        var state = policy.Get();
        Assert.True(changed);
        Assert.False(state.PollingStarted);
        Assert.True(state.RequiresPresetExecution);
        Assert.False(state.ReadyForPreset);
        Assert.False(state.PresetCompleted);
        Assert.Null(state.LastProbeValue);
        Assert.Null(state.LastProbeAt);
        Assert.False(policy.IsOpen(Morning.AddDays(1).DateTime));
    }

    [Fact]
    public void RefreshOnSameDay_DoesNotChangeState()
    {
        var policy = CreatePolicy(Morning);
        policy.MarkPresetCompleted();
        var before = policy.Get();

        var changed = policy.RefreshDailyWindowState();

        Assert.False(changed);
        Assert.Equal(before, policy.Get());
    }

    [Fact]
    public void StartPollingAfterDirectCompletion_DoesNotRestartPolling()
    {
        var policy = CreatePolicy(Morning);
        policy.MarkPresetCompleted();

        policy.StartPolling();

        var state = policy.Get();
        Assert.False(state.PollingStarted);
        Assert.False(state.RequiresPresetExecution);
        Assert.False(state.ReadyForPreset);
    }

    private static DailyWindowPolicy CreatePolicy(
        DateTimeOffset currentTime,
        bool enabled = true,
        int startHour = 9,
        int startMinute = 30)
    {
        return CreatePolicy(new ManualTimeProvider(currentTime), enabled, startHour, startMinute);
    }

    private static DailyWindowPolicy CreatePolicy(
        TimeProvider timeProvider,
        bool enabled = true,
        int startHour = 9,
        int startMinute = 30)
    {
        var options = new PresetGateOptions(
            Enabled: enabled,
            StartHour: startHour,
            StartMinute: startMinute,
            PollIntervalSeconds: 60,
            ProbeSql: "SELECT 0");

        return new DailyWindowPolicy(options, timeProvider);
    }
}
