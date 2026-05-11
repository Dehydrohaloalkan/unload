using Unload.Api.Models;

namespace Unload.Api.UseCases.Abstractions;

public interface IRequeueToMqUseCase
{
    Task<RequeueToMqResponse> ExecuteAsync(RequeueToMqRequest request, CancellationToken cancellationToken);
}

