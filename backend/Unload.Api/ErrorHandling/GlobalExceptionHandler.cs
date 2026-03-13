using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Unload.Application;

namespace Unload.Api.ErrorHandling;

/// <summary>
/// Глобальный обработчик исключений API с единым ProblemDetails-контрактом.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title, errorCode) = MapException(exception);

        _logger.LogError(
            exception,
            "Request failed with {ErrorCode}. Path: {Path}, TraceId: {TraceId}",
            errorCode,
            httpContext.Request.Path.Value,
            httpContext.TraceIdentifier);

        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.com/{statusCode}",
            Title = title,
            Status = statusCode,
            Detail = exception.Message,
            Instance = httpContext.Request.Path
        };

        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static (int StatusCode, string Title, string ErrorCode) MapException(Exception exception)
    {
        return exception switch
        {
            RunAlreadyInProgressException => (StatusCodes.Status409Conflict, "Run conflict", "RUN_ALREADY_IN_PROGRESS"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Validation error", "VALIDATION_ERROR"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Business rule violation", "BUSINESS_RULE_VIOLATION"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error", "UNEXPECTED_ERROR")
        };
    }
}
