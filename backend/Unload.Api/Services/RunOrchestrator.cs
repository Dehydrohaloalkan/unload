using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Unload.Core;

namespace Unload.Api;

public class RunOrchestrator : IRunOrchestrator
{
    private static readonly Regex ProfileCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);

    private readonly IRunRequestFactory _requestFactory;
    private readonly IRunner _runner;
    private readonly IHubContext<RunStatusHub> _hubContext;
    private readonly ILogger<RunOrchestrator> _logger;
    private readonly string _outputDirectory;

    public RunOrchestrator(
        IRunRequestFactory requestFactory,
        IRunner runner,
        IHubContext<RunStatusHub> hubContext,
        ILogger<RunOrchestrator> logger,
        string outputDirectory)
    {
        _requestFactory = requestFactory;
        _runner = runner;
        _hubContext = hubContext;
        _logger = logger;
        _outputDirectory = Path.GetFullPath(outputDirectory);
    }

    public string StartRun(IReadOnlyCollection<string> profileCodes)
    {
        var normalizedCodes = NormalizeProfileCodes(profileCodes);
        var request = _requestFactory.Create(normalizedCodes, _outputDirectory);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var @event in _runner.RunAsync(request, CancellationToken.None))
                {
                    _logger.LogInformation(
                        "Run {CorrelationId}: {Step} {Message}",
                        @event.CorrelationId,
                        @event.Step,
                        @event.Message);

                    await _hubContext.Clients
                        .Group(request.CorrelationId)
                        .SendAsync("status", @event, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run '{CorrelationId}' failed in orchestrator.", request.CorrelationId);
            }
        });

        return request.CorrelationId;
    }

    private static IReadOnlyCollection<string> NormalizeProfileCodes(IReadOnlyCollection<string> profileCodes)
    {
        var normalized = profileCodes
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("At least one profile code is required.");
        }

        foreach (var code in normalized)
        {
            if (!ProfileCodePattern.IsMatch(code))
            {
                throw new InvalidOperationException($"Profile code '{code}' is invalid.");
            }
        }

        return normalized;
    }
}
