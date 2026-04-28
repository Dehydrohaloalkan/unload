using System.Collections.ObjectModel;

namespace Unload.Api.ErrorHandling;

public class ApiProblemException(
    int statusCode,
    string title,
    string detail,
    string errorCode,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;

    public string Title { get; } = title;

    public string ErrorCode { get; } = errorCode;

    public IReadOnlyDictionary<string, object?>? Extensions { get; } = extensions is null
            ? null
            : new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(extensions));
}
