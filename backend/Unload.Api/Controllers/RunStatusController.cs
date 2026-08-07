using Microsoft.AspNetCore.Mvc;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Api.Controllers;

/// <summary>
/// Чтение текущего состояния main и extra запусков.
/// </summary>
[ApiController]
[Route("api/runs")]
public class RunStatusController(
    RunActivationChannel runWorkflow,
    RunStateStore runStateStore) : ControllerBase
{
    private readonly RunActivationChannel _runWorkflow = runWorkflow;
    private readonly RunStateStore _runStateStore = runStateStore;

    [HttpGet]
    public IActionResult GetRuns()
    {
        return Ok(_runStateStore.List());
    }

    [HttpGet("active")]
    public IActionResult GetActiveRun()
    {
        var correlationId = _runWorkflow.GetActiveCorrelationId();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return Ok(new { correlationId = (string?)null });
        }

        var run = _runStateStore.Get(correlationId);
        return run is null
            ? Ok(new { correlationId })
            : Ok(run);
    }

    [HttpGet("{correlationId}")]
    public IActionResult GetRunByCorrelationId(string correlationId)
    {
        var run = _runStateStore.Get(correlationId);
        return run is null ? NotFound() : Ok(run);
    }
}
