using Microsoft.AspNetCore.Mvc;

namespace Unload.Api.ErrorHandling.Abstractions;

public interface IApiProblemDetailsFactory
{
    ProblemDetails Create(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        string errorCode,
        IReadOnlyDictionary<string, object?>? extensions = null);
}

