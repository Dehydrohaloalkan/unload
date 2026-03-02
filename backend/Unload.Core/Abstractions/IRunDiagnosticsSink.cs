namespace Unload.Core;

public interface IRunDiagnosticsSink
{
    Task WriteEventAsync(RunnerEvent @event, CancellationToken cancellationToken);

    Task WriteMetricAsync(RunMetricRecord metric, CancellationToken cancellationToken);
}
