using Microsoft.AspNetCore.SignalR;
using Unload.Store;

namespace Unload.Api.Services;

public interface IRunStatusLiveTransport
{
    Task SendAsync(RunStatusInfo state, CancellationToken cancellationToken);
}

public sealed class SignalRRunStatusLiveTransport(IHubContext<RunStatusHub> hubContext)
    : IRunStatusLiveTransport
{
    public Task SendAsync(RunStatusInfo state, CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendRunStatusAsync(state, cancellationToken);
}

/// <summary>
/// Последовательно публикует компактные снимки run и не допускает регрессии после terminal-состояния.
/// </summary>
public sealed class RunStatusLivePublisher(
    IRunStatusLiveTransport transport,
    ILogger<RunStatusLivePublisher> logger)
{
    internal const int RetainedRunCapacity = 1_024;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Dictionary<string, PublishedState> _published = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _publishedOrder = new();

    public async Task PublishAsync(RunStatusInfo state)
    {
        await _sendLock.WaitAsync();
        try
        {
            if (_published.TryGetValue(state.CorrelationId, out var previous) &&
                (previous.IsTerminal || state.UpdatedAt < previous.UpdatedAt))
            {
                return;
            }

            try
            {
                await transport.SendAsync(state, CancellationToken.None);
                if (!_published.ContainsKey(state.CorrelationId))
                {
                    _publishedOrder.Enqueue(state.CorrelationId);
                }

                _published[state.CorrelationId] = new PublishedState(state.UpdatedAt, IsTerminal(state.Status));
                while (_published.Count > RetainedRunCapacity)
                {
                    _published.Remove(_publishedOrder.Dequeue());
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to publish live run snapshot. CorrelationId: {CorrelationId}",
                    state.CorrelationId);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static bool IsTerminal(RunLifecycleStatus status) =>
        status is RunLifecycleStatus.Completed or RunLifecycleStatus.Failed or RunLifecycleStatus.Cancelled;

    private sealed record PublishedState(DateTimeOffset UpdatedAt, bool IsTerminal);
}
