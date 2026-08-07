using Microsoft.AspNetCore.Mvc;
using Unload.Bootstrapper;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Api.Controllers;

/// <summary>
/// Dashboard и исторические представления workflow.
/// </summary>
[ApiController]
[Route("api/runs")]
public class RunHistoryController(
    WorkflowQueryService workflowQueryService,
    HistoryRetentionOptions historyRetentionOptions) : ControllerBase
{
    private readonly WorkflowQueryService _workflowQueryService = workflowQueryService;
    private readonly HistoryRetentionOptions _historyRetentionOptions = historyRetentionOptions;

    [HttpGet("today")]
    public ActionResult<IReadOnlyList<RunStatusInfo>> GetTodayRuns()
    {
        return Ok(_workflowQueryService.GetTodayRuns());
    }

    [HttpGet("dashboard")]
    public ActionResult<WorkflowDashboard> GetWorkflowDashboard()
    {
        return Ok(_workflowQueryService.GetDashboard());
    }

    [HttpGet("history")]
    public ActionResult<WorkflowHistory> GetWorkflowHistory([FromQuery] int? days)
    {
        var requestedDays = days ?? _historyRetentionOptions.RetentionDays;
        return Ok(_workflowQueryService.GetHistory(requestedDays));
    }
}
