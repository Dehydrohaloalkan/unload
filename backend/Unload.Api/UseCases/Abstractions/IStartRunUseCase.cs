using Unload.Api.Models;

namespace Unload.Api.UseCases.Abstractions;

public interface IStartRunUseCase
{
    Task<RunAcceptedResponse> ExecuteAsync(RunStartRequest request, CancellationToken cancellationToken);
}

