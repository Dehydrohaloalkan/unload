using System.Collections.ObjectModel;

namespace Unload.Api.ErrorHandling;

public  class ApiProblemException : Exception
{
    public ApiProblemException(
        int statusCode,
        string title,
        string detail,
        string errorCode,
        IReadOnlyDictionary<string, object?>? extensions = null)
        : base(detail)
    {
        StatusCode = statusCode;
        Title = title;
        ErrorCode = errorCode;
        Extensions = extensions is null
            ? null
            : new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(extensions));
    }

    public int StatusCode { get; }

    public string Title { get; }

    public string ErrorCode { get; }

    public IReadOnlyDictionary<string, object?>? Extensions { get; }
}
