using Unload.Api.Models;

namespace Unload.Api.UseCases.Abstractions;

public interface IRequeueToGatewayUseCase
{
    Task<RequeueToGatewayResponse> ExecuteAsync(RequeueToGatewayRequest request, CancellationToken cancellationToken);
}
