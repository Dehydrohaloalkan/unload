using System.Text.RegularExpressions;
using Unload.Core;

namespace Unload.Application;

public class RunOrchestrator : IRunOrchestrator
{
    private static readonly Regex ProfileCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);

    private readonly IRunRequestFactory _requestFactory;
    private readonly IRunQueue _runQueue;
    private readonly IRunStateStore _runStateStore;
    private readonly string _outputDirectory;

    public RunOrchestrator(
        IRunRequestFactory requestFactory,
        IRunQueue runQueue,
        IRunStateStore runStateStore,
        string outputDirectory)
    {
        _requestFactory = requestFactory;
        _runQueue = runQueue;
        _runStateStore = runStateStore;
        _outputDirectory = Path.GetFullPath(outputDirectory);
    }

    public string StartRun(IReadOnlyCollection<string> profileCodes)
    {
        var normalizedCodes = NormalizeProfileCodes(profileCodes);
        var request = _requestFactory.Create(normalizedCodes, _outputDirectory);
        _runStateStore.SetQueued(request.CorrelationId, normalizedCodes);
        if (!_runQueue.TryEnqueue(request))
        {
            throw new InvalidOperationException("Run queue is full. Please retry later.");
        }

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
