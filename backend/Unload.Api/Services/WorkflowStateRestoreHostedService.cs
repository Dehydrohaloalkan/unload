using Unload.Api.Abstractions;

namespace Unload.Api.Services;

/// <summary>
/// Performs synchronous state restore during host startup to avoid
/// races where API requests arrive before in-memory workflow state is reconstructed.
/// </summary>
public sealed class WorkflowStateRestoreHostedService(
    IWorkflowInMemoryStateRestorer restorer,
    ILogger<WorkflowStateRestoreHostedService> logger) : IHostedService
{
    private readonly IWorkflowInMemoryStateRestorer _restorer = restorer;
    private readonly ILogger<WorkflowStateRestoreHostedService> _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _restorer.RestoreForToday();
        _logger.LogInformation("Workflow in-memory state restored from history.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

