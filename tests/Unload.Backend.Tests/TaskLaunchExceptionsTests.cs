using Microsoft.AspNetCore.Http;
using Unload.Api.ErrorHandling;
using Unload.Tasks;

namespace Unload.Backend.Tests;

public class TaskLaunchExceptionsTests
{
    [Fact]
    public void ValidationFailure_MapsToBadRequestAndKeepsDiagnostics()
    {
        var extensions = new Dictionary<string, object?>
        {
            ["unknownCodes"] = new[] { "UNKNOWN" }
        };
        var source = new TaskLaunchException(
            TaskLaunchFailureKind.Validation,
            "UNKNOWN_MEMBER_CODES",
            "Unknown member code.",
            extensions);

        var result = TaskLaunchExceptions.ToApiProblem(source, "Run conflict");

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Equal("Validation error", result.Title);
        Assert.Equal(source.Message, result.Message);
        Assert.Equal(source.ErrorCode, result.ErrorCode);
        Assert.NotSame(extensions, result.Extensions);
        var unknownCodes = Assert.IsType<string[]>(result.Extensions!["unknownCodes"]);
        Assert.Equal(["UNKNOWN"], unknownCodes);
    }

    [Theory]
    [InlineData("Run conflict")]
    [InlineData("Preset task conflict")]
    [InlineData("Extra task conflict")]
    public void ConflictFailure_MapsToConflictAndKeepsOperationTitle(string conflictTitle)
    {
        var source = new TaskLaunchException(
            TaskLaunchFailureKind.Conflict,
            "PRESET_GATE_BLOCKED",
            "Daily window is closed.");

        var result = TaskLaunchExceptions.ToApiProblem(source, conflictTitle);

        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(conflictTitle, result.Title);
        Assert.Equal(source.Message, result.Message);
        Assert.Equal(source.ErrorCode, result.ErrorCode);
    }
}
