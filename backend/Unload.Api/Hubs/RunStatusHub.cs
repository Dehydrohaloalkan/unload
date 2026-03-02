using Microsoft.AspNetCore.SignalR;

namespace Unload.Api;

public class RunStatusHub : Hub
{
    public Task SubscribeRun(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new HubException("CorrelationId is required.");
        }

        return Groups.AddToGroupAsync(Context.ConnectionId, correlationId.Trim());
    }

    public Task UnsubscribeRun(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new HubException("CorrelationId is required.");
        }

        return Groups.RemoveFromGroupAsync(Context.ConnectionId, correlationId.Trim());
    }
}
