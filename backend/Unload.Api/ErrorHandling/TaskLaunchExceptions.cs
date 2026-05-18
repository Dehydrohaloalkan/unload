using Unload.Tasks;

namespace Unload.Api.ErrorHandling;

/// <summary>
/// Маппинг <see cref="TaskLaunchException"/> → <see cref="ApiProblemException"/>.
/// </summary>
public static class TaskLaunchExceptions
{
    public static ApiProblemException ToApiProblem(
        TaskLaunchException exception,
        string conflictTitle)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(conflictTitle);

        var isValidation = exception.FailureKind == TaskLaunchFailureKind.Validation;
        return new ApiProblemException(
            isValidation ? StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict,
            isValidation ? "Validation error" : conflictTitle,
            exception.Message,
            exception.ErrorCode,
            exception.Extensions);
    }
}
