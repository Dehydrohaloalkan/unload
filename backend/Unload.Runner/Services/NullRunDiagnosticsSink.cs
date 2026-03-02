using Unload.Core;

namespace Unload.Runner;

public class NullRunDiagnosticsSink : IRunDiagnosticsSink
{
    public Task WriteEventAsync(RunnerEvent @event, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task WriteMetricAsync(RunMetricRecord metric, CancellationToken cancellationToken) => Task.CompletedTask;
}
