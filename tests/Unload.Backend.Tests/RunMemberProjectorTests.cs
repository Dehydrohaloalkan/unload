using Unload.Core;
using Unload.Store;

namespace Unload.Backend.Tests;

public class RunMemberProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RunnerStep.QueryStarted, MemberRunLifecycleStatus.Running)]
    [InlineData(RunnerStep.ScriptCompleted, MemberRunLifecycleStatus.Completed)]
    [InlineData(RunnerStep.Failed, MemberRunLifecycleStatus.Failed)]
    public void Apply_MapsMemberEvent(RunnerStep step, MemberRunLifecycleStatus expectedStatus)
    {
        var result = RunMemberProjector.Apply(
            source: null,
            Event(step, memberName: " Member A "),
            Now.AddMinutes(1));

        var member = Assert.Single(result).Value;
        Assert.Equal("Member A", member.MemberName);
        Assert.Equal(expectedStatus, member.Status);
        Assert.Equal(step, member.LastStep);
    }

    [Theory]
    [InlineData(RunnerStep.Completed, MemberRunLifecycleStatus.Completed)]
    [InlineData(RunnerStep.Failed, MemberRunLifecycleStatus.Failed)]
    public void Apply_GlobalTerminalEventUpdatesAllMembers(
        RunnerStep step,
        MemberRunLifecycleStatus expectedStatus)
    {
        var original = Member("Member A", MemberRunLifecycleStatus.Running);
        var source = Members(original);

        var result = RunMemberProjector.Apply(source, Event(step), Now.AddMinutes(1));

        Assert.Equal(expectedStatus, result["Member A"].Status);
        Assert.Same(original, source["Member A"]);
    }

    private static RunnerEvent Event(RunnerStep step, string? memberName = null)
    {
        return new RunnerEvent(Now, "run-1", step, step.ToString(), memberName);
    }

    private static MemberRunStatusInfo Member(string name, MemberRunLifecycleStatus status)
    {
        return new MemberRunStatusInfo(name, status, RunnerStep.QueryStarted, "started", Now);
    }

    private static IReadOnlyDictionary<string, MemberRunStatusInfo> Members(MemberRunStatusInfo member)
    {
        return new Dictionary<string, MemberRunStatusInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [member.MemberName] = member
        };
    }
}
