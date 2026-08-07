using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class RunArtifactProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Apply_FileWrittenAddsArtifactMetadata()
    {
        var result = RunArtifactProjector.Apply(
            source: null,
            Event(RunnerStep.FileWritten, "/tmp/result.txt"));

        var artifact = Assert.Single(result);
        Assert.Equal("result.txt", artifact.FileName);
        Assert.Equal("/tmp/result.txt", artifact.FilePath);
        Assert.Equal("Member A", artifact.MemberName);
        Assert.Equal("script-1", artifact.ScriptCode);
    }

    [Fact]
    public void Apply_IgnoresOtherStepsAndCaseInsensitiveDuplicates()
    {
        var original = new RunOutputArtifactInfo("result.txt", "/tmp/RESULT.txt", "Member A", "script-1", Now);
        IReadOnlyCollection<RunOutputArtifactInfo> source = [original];

        var ignored = RunArtifactProjector.Apply(source, Event(RunnerStep.QueryCompleted, "/tmp/other.txt"));
        var duplicate = RunArtifactProjector.Apply(source, Event(RunnerStep.FileWritten, "/tmp/result.txt"));

        Assert.Same(original, Assert.Single(ignored));
        Assert.Same(original, Assert.Single(duplicate));
        Assert.Same(original, Assert.Single(source));
    }

    private static RunnerEvent Event(RunnerStep step, string filePath)
    {
        return new RunnerEvent(
            Now,
            "run-1",
            step,
            step.ToString(),
            MemberName: "Member A",
            ScriptCode: "script-1",
            FilePath: filePath);
    }
}
