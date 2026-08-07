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
    public ActionResult<IReadOnlyCollection<RunStatusInfo>> GetRuns()
    {
        return Ok(_runStateStore.List());
    }

    [HttpGet("active")]
    [ProducesResponseType<RunStatusInfo>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RunStatusInfo> GetActiveRun()
    {
        var correlationId = _runWorkflow.GetActiveCorrelationId();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return NotFound();
        }

        var run = _runStateStore.Get(correlationId);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("{correlationId}")]
    [ProducesResponseType<RunStatusInfo>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<RunStatusInfo> GetRunByCorrelationId(string correlationId)
    {
        var run = _runStateStore.Get(correlationId);
        return run is null ? NotFound() : Ok(run);
    }
}
