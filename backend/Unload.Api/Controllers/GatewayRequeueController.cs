using Microsoft.AspNetCore.Mvc;
using Unload.Store;

namespace Unload.Api.Controllers;

/// <summary>
/// Повторная публикация готовых результатов в gateway.
/// </summary>
[ApiController]
[Route("api/runs")]
public class GatewayRequeueController(RequeueService requeueService) : ControllerBase
{
    private readonly RequeueService _requeueService = requeueService;

    [HttpPost("requeue")]
    public async Task<ActionResult<RequeueToGatewayResponse>> RequeueToGatewayAsync(
        [FromBody] RequeueToGatewayRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _requeueService.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }
}
