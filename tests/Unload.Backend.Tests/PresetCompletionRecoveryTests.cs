using Unload.Store;
using Unload.Tasks;

namespace Unload.Backend.Tests;

public class PresetCompletionRecoveryTests
{
    private static readonly DateTimeOffset Today = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PresetCompletedToday_RestoresOpenWindowAfterRestart()
    {
        using var fixture = new RecoveryFixture(Today);
        fixture.AddPresetHistory(Today);

        var restored = fixture.Recovery.RestoreIfCompletedToday();

        Assert.True(restored);
        Assert.True(fixture.Policy.Get().PresetCompleted);
        Assert.True(fixture.Policy.IsOpen(Today.DateTime));
    }

    [Fact]
    public void PresetCompletedYesterday_DoesNotRestoreOpenWindow()
    {
        using var fixture = new RecoveryFixture(Today);
        fixture.AddPresetHistory(Today.AddDays(-1));

        var restored = fixture.Recovery.RestoreIfCompletedToday();

        Assert.False(restored);
        Assert.False(fixture.Policy.Get().PresetCompleted);
        Assert.False(fixture.Policy.IsOpen(Today.DateTime));
    }

    [Fact]
    public void DisabledGate_DoesNotRestorePresetCompletion()
    {
        using var fixture = new RecoveryFixture(Today, enabled: false);
        fixture.AddPresetHistory(Today);

        var restored = fixture.Recovery.RestoreIfCompletedToday();

        Assert.False(restored);
        Assert.False(fixture.Policy.Get().PresetCompleted);
    }

    [Fact]
    public void AlreadyRestoredState_IsNotChangedAgain()
    {
        using var fixture = new RecoveryFixture(Today);
        fixture.AddPresetHistory(Today);
        Assert.True(fixture.Recovery.RestoreIfCompletedToday());
        var state = fixture.Policy.Get();

        var restoredAgain = fixture.Recovery.RestoreIfCompletedToday();

        Assert.False(restoredAgain);
        Assert.Equal(state, fixture.Policy.Get());
    }

    private sealed class RecoveryFixture : IDisposable
    {
        public RecoveryFixture(DateTimeOffset currentTime, bool enabled = true)
        {
            TimeProvider = new ManualTimeProvider(currentTime);
            ScratchDirectory = Path.Combine(Path.GetTempPath(), $"unload-preset-recovery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(ScratchDirectory);
            History = new TaskExecutionHistoryStore(Path.Combine(ScratchDirectory, "history.json"));
            var options = new PresetGateOptions(enabled, 9, 0, 60, "SELECT 0");
            Policy = new DailyWindowPolicy(options, TimeProvider);
            Recovery = new PresetCompletionRecovery(options, Policy, History, TimeProvider);
        }

        public string ScratchDirectory { get; }

        public ManualTimeProvider TimeProvider { get; }

        public TaskExecutionHistoryStore History { get; }

        public DailyWindowPolicy Policy { get; }

        public PresetCompletionRecovery Recovery { get; }

        public void AddPresetHistory(DateTimeOffset completedAt)
        {
            History.Add(TaskCodes.Preset, completedAt.AddMinutes(-1), completedAt, null, "completed");
        }

        public void Dispose()
        {
            Directory.Delete(ScratchDirectory, recursive: true);
        }
    }
}
