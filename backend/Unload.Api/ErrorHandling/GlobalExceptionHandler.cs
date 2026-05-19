using Microsoft.AspNetCore.Diagnostics;
using Unload.Tasks.MainUnload;

namespace Unload.Api.ErrorHandling;

/// <summary>
/// Глобальный обработчик исключений API с единым ProblemDetails-контрактом.
/// </summary>
public class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    ApiProblemDetailsFactory problemFactory) : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger = logger;
    private readonly ApiProblemDetailsFactory _problemFactory = problemFactory;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var mapped = MapException(exception);

        _logger.LogError(
            exception,
            "Request failed with {ErrorCode}. Path: {Path}, TraceId: {TraceId}",
            mapped.ErrorCode,
            httpContext.Request.Path.Value,
            httpContext.TraceIdentifier);

        var problem = _problemFactory.Create(
            httpContext,
            mapped.StatusCode,
            mapped.Title,
            mapped.Detail,
            mapped.ErrorCode,
            mapped.Extensions);

        httpContext.Response.StatusCode = mapped.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static ExceptionMapping MapException(Exception exception)
    {
        return exception switch
        {
            ApiProblemException apiProblem => new ExceptionMapping(
                apiProblem.StatusCode,
                apiProblem.Title,
                apiProblem.ErrorCode,
                apiProblem.Message,
                apiProblem.Extensions),
            RunAlreadyInProgressException runConflict => new ExceptionMapping(
                StatusCodes.Status409Conflict,
                "Run conflict",
                "RUN_ALREADY_IN_PROGRESS",
                runConflict.Message,
                runConflict.ActiveCorrelationId is null
                    ? null
                    : new Dictionary<string, object?> { ["activeCorrelationId"] = runConflict.ActiveCorrelationId }),
            ArgumentException argumentException => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "VALIDATION_ERROR",
                argumentException.Message),
            InvalidOperationException invalidOperationException => new ExceptionMapping(
                StatusCodes.Status409Conflict,
                "Business rule violation",
                "BUSINESS_RULE_VIOLATION",
                invalidOperationException.Message),
            _ => new ExceptionMapping(
                StatusCodes.Status500InternalServerError,
                "Unexpected server error",
                "UNEXPECTED_ERROR",
                "Unexpected server error.")
        };
    }

    private  record ExceptionMapping(
        int StatusCode,
        string Title,
        string ErrorCode,
        string Detail,
        IReadOnlyDictionary<string, object?>? Extensions = null);
}
