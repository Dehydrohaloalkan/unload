namespace Unload.Api;

public record RunStartRequest(IReadOnlyCollection<string> ProfileCodes);

public record RunAcceptedResponse(
    string CorrelationId,
    string HubPath,
    string SubscribeMethod,
    string EventName);
