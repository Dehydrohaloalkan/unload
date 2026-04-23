using Unload.Api.Models;

namespace Unload.Api.UseCases.Abstractions;

public interface IGetServerTimeUseCase
{
    ServerTimeResponse Execute();
}

