namespace Unload.Api.UseCases;

public interface IStartRunUseCase
{
    Task<RunAcceptedResponse> ExecuteAsync(RunStartRequest request, CancellationToken cancellationToken);
}

