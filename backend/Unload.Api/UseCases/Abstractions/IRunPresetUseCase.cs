using Unload.TaskFlow;

namespace Unload.Api.UseCases.Abstractions;

public interface IRunPresetUseCase
{
    Task<ScriptTaskRunResult> ExecuteAsync(bool adminOverride, CancellationToken cancellationToken);
}

