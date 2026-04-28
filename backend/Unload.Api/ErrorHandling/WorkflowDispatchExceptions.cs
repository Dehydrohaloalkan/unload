using Unload.TaskFlow;
using Unload.TaskFlow.Exceptions;

namespace Unload.Api.ErrorHandling;

public static class WorkflowDispatchExceptions
{
    public static ApiProblemException ToApiProblem(
        WorkflowTaskDispatchException exception,
        string conflictTitle)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(conflictTitle);

        var isValidation = exception.FailureKind == WorkflowTaskFailureKind.Validation;
        return new ApiProblemException(
            isValidation ? StatusCodes.Status400BadRequest : StatusCodes.Status409Conflict,
            isValidation ? "Validation error" : conflictTitle,
            exception.Message,
            exception.ErrorCode,
            exception.Extensions);
    }
}
