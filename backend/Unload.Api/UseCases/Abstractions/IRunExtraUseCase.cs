using Unload.TaskFlow;

namespace Unload.Api.UseCases.Abstractions;

public interface IRunExtraUseCase
{
    Task<ScriptTaskRunResult> ExecuteAsync(bool adminOverride, CancellationToken cancellationToken);
}

