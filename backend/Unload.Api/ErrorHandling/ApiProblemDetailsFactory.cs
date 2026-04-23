using Microsoft.AspNetCore.Mvc;

namespace Unload.Api.ErrorHandling;

public  class ApiProblemDetailsFactory : IApiProblemDetailsFactory
{
    public ProblemDetails Create(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        string errorCode,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.com/{statusCode}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        if (extensions is not null)
        {
            foreach (var (key, extensionValue) in extensions)
            {
                problem.Extensions[key] = extensionValue;
            }
        }

        return problem;
    }
}
