using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.Store;

public class RequeueService(
    RunStateStore runStateStore,
    TaskExecutionHistoryStore taskExecutionHistoryStore,
    IGatewayPublisher gatewayPublisher,
    ILogger<RequeueService> logger)
{
    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly TaskExecutionHistoryStore _taskExecutionHistoryStore = taskExecutionHistoryStore;
    private readonly IGatewayPublisher _gatewayPublisher = gatewayPublisher;
    private readonly ILogger<RequeueService> _logger = logger;

    private static readonly ConcurrentDictionary<string, (DateTimeOffset SavedAt, RequeueToGatewayResponse Response)> IdempotencyCache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<RequeueToGatewayResponse> ExecuteAsync(RequeueToGatewayRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var items = request.Items ?? Array.Empty<RequeueItem>();
        if (items.Count == 0)
        {
            return new RequeueToGatewayResponse(
                RequestId: "empty",
                AcceptedBatches: 0,
                FailedBatches: 0,
                Results: Array.Empty<RequeueItemResult>());
        }

        var requestId = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey.Trim();

        EvictExpired();

        if (IdempotencyCache.TryGetValue(requestId, out var cached))
        {
            return cached.Response;
        }

        var now = DateTimeOffset.UtcNow;
        var results = new List<RequeueItemResult>(items.Count);
        var totalAccepted = 0;
        var totalFailed = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var itemResult = await HandleItemAsync(item, request.DryRun, now, cancellationToken);
            results.Add(itemResult);
            totalAccepted += itemResult.AcceptedBatches;
            totalFailed += itemResult.FailedBatches;
        }

        var response = new RequeueToGatewayResponse(
            RequestId: requestId,
            AcceptedBatches: totalAccepted,
            FailedBatches: totalFailed,
            Results: results);

        IdempotencyCache[requestId] = (DateTimeOffset.UtcNow, response);
        return response;
    }

    private async Task<RequeueItemResult> HandleItemAsync(
        RequeueItem item,
        bool dryRun,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var taskCode = (item.TaskCode ?? string.Empty).Trim().ToLowerInvariant();
        var correlationId = (item.CorrelationId ?? string.Empty).Trim();
        var memberFilter = new HashSet<string>(
            (item.MemberNames ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var hasMemberFilter = memberFilter.Count > 0;
        // Пути храним как пришли (только trim): абсолютные сравниваются по полному пути,
        // относительные (относительно output-корня) — по суффиксу. См. MatchesFileFilter.
        var fileFilter = new HashSet<string>(
            (item.FilePaths ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var hasFileFilter = fileFilter.Count > 0;

        if (string.IsNullOrWhiteSpace(taskCode) || string.IsNullOrWhiteSpace(correlationId))
        {
            return new RequeueItemResult(
                TaskCode: taskCode,
                CorrelationId: correlationId,
                AcceptedBatches: 0,
                FailedBatches: 1,
                Batches:
                [
                    new RequeueBatchResult(
                        MemberName: "unknown",
                        BatchId: "invalid",
                        Status: SenderBatchStatus.Failed,
                        Message: "TaskCode and CorrelationId are required.")
                ]);
        }

        try
        {
            var batchDescriptors = taskCode switch
            {
                "run" => BuildRunBatches(
                    correlationId,
                    hasMemberFilter ? memberFilter : null,
                    hasFileFilter ? fileFilter : null),
                "extra" => BuildExtraBatches(
                    correlationId,
                    hasMemberFilter ? memberFilter : null,
                    hasFileFilter ? fileFilter : null),
                _ => throw new InvalidOperationException($"Unsupported taskCode '{taskCode}'.")
            };

            if (batchDescriptors.Count == 0)
            {
                return new RequeueItemResult(
                    TaskCode: taskCode,
                    CorrelationId: correlationId,
                    AcceptedBatches: 0,
                    FailedBatches: 0,
                    Batches: Array.Empty<RequeueBatchResult>());
            }

            var accepted = 0;
            var failed = 0;
            var batches = new List<RequeueBatchResult>(batchDescriptors.Count);

            foreach (var (memberName, files) in batchDescriptors)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batchId = $"requeue-{Guid.NewGuid():N}"[..24];
                var evt = new SenderFileBatchReadyEvent(
                    OccurredAt: occurredAt,
                    CorrelationId: correlationId,
                    MemberName: memberName,
                    BatchId: batchId,
                    Version: 1,
                    Files: files);

                if (!dryRun)
                {
                    await _gatewayPublisher.PublishFileBatchReadyAsync(evt, cancellationToken);
                }

                accepted++;
                batches.Add(new RequeueBatchResult(
                    MemberName: memberName,
                    BatchId: batchId,
                    Status: SenderBatchStatus.Ready,
                    Message: dryRun ? "Dry run: not published." : "Published to gateway."));
            }

            return new RequeueItemResult(
                TaskCode: taskCode,
                CorrelationId: correlationId,
                AcceptedBatches: accepted,
                FailedBatches: failed,
                Batches: batches);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Requeue failed. TaskCode: {TaskCode}, CorrelationId: {CorrelationId}", taskCode, correlationId);
            return new RequeueItemResult(
                TaskCode: taskCode,
                CorrelationId: correlationId,
                AcceptedBatches: 0,
                FailedBatches: 1,
                Batches:
                [
                    new RequeueBatchResult(
                        MemberName: "unknown",
                        BatchId: "failed",
                        Status: SenderBatchStatus.Failed,
                        Message: ex.Message)
                ]);
        }
    }

    private IReadOnlyList<(string MemberName, IReadOnlyCollection<SenderFileDescriptor> Files)> BuildRunBatches(
        string correlationId,
        HashSet<string>? memberFilter,
        HashSet<string>? fileFilter)
    {
        var run = _runStateStore.Get(correlationId);
        if (run is null)
        {
            throw new InvalidOperationException("Run was not found.");
        }

        var artifacts = run.OutputArtifacts ?? Array.Empty<RunOutputArtifactInfo>();
        var grouped = artifacts
            .Where(x => !string.IsNullOrWhiteSpace(x.MemberName))
            .GroupBy(x => x.MemberName!.Trim(), StringComparer.OrdinalIgnoreCase);

        var batches = new List<(string, IReadOnlyCollection<SenderFileDescriptor>)>();
        foreach (var group in grouped)
        {
            var memberName = group.Key;
            if (memberFilter is not null && !memberFilter.Contains(memberName))
            {
                continue;
            }

            var files = group
                .Select(x => x.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Where(path => MatchesFileFilter(path, fileFilter))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(CreateDescriptorOrThrow)
                .ToArray();

            if (files.Length > 0)
            {
                batches.Add((memberName, files));
            }
        }

        return batches;
    }

    private IReadOnlyList<(string MemberName, IReadOnlyCollection<SenderFileDescriptor> Files)> BuildExtraBatches(
        string correlationId,
        HashSet<string>? memberFilter,
        HashSet<string>? fileFilter)
    {
        var record = _taskExecutionHistoryStore.TryGetByCorrelationId(correlationId);
        if (record is null || !string.Equals(record.TaskCode, "extra", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Extra history record was not found.");
        }

        if (string.IsNullOrWhiteSpace(record.OutputPath))
        {
            throw new InvalidOperationException("Extra output path is missing.");
        }

        var baseDir = Path.GetFullPath(record.OutputPath.Trim());
        var filesDir = Path.Combine(baseDir, "output-files");
        if (!Directory.Exists(filesDir))
        {
            throw new DirectoryNotFoundException($"Extra output files directory was not found: {filesDir}");
        }

        // Раскладка extra: output-files/<scriptCode>/<bank>/<file> — обходим вложенные каталоги.
        var files = Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories)
            .Where(path => MatchesFileFilter(path, fileFilter))
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Партия шлюза — на скрипт (MemberName = код скрипта), как при исходной публикации.
        var grouped = files.GroupBy(
            path => ResolveExtraScriptCode(filesDir, path),
            StringComparer.OrdinalIgnoreCase);

        var batches = new List<(string, IReadOnlyCollection<SenderFileDescriptor>)>();
        foreach (var group in grouped)
        {
            var memberName = string.IsNullOrWhiteSpace(group.Key) ? "UNKNOWN" : group.Key;
            if (memberFilter is not null && !memberFilter.Contains(memberName))
            {
                continue;
            }

            var descriptors = group.Select(CreateDescriptorOrThrow).ToArray();
            if (descriptors.Length > 0)
            {
                batches.Add((memberName, descriptors));
            }
        }

        return batches;
    }

    private static SenderFileDescriptor CreateDescriptorOrThrow(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException("File was not found.", full);
        }

        var info = new FileInfo(full);
        return new SenderFileDescriptor(
            FilePath: full,
            FileName: info.Name,
            SizeBytes: info.Length);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }

    /// <summary>Код скрипта extra-файла — первый сегмент пути относительно <c>output-files</c>.</summary>
    private static string ResolveExtraScriptCode(string filesDir, string filePath)
    {
        var relative = Path.GetRelativePath(filesDir, filePath);
        var separatorIndex = relative.IndexOfAny(['\\', '/']);
        var segment = separatorIndex > 0 ? relative[..separatorIndex] : Path.GetFileNameWithoutExtension(relative);
        return string.IsNullOrWhiteSpace(segment) ? "UNKNOWN" : segment.Trim();
    }

    /// <summary>
    /// Сопоставляет файл с фильтром. UI может прислать как абсолютный путь (из артефактов run'а),
    /// так и путь относительно output-корня (из диск-скана) — относительные сравниваем по суффиксу.
    /// </summary>
    private static bool MatchesFileFilter(string filePath, HashSet<string>? fileFilter)
    {
        if (fileFilter is null)
        {
            return true;
        }

        var fullPath = NormalizePath(filePath);
        var candidate = CanonicalPathKey(fullPath);
        return fileFilter.Any(entry =>
        {
            if (Path.IsPathRooted(entry))
            {
                return string.Equals(NormalizePath(entry), fullPath, StringComparison.OrdinalIgnoreCase);
            }

            var key = CanonicalPathKey(entry);
            return candidate.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string CanonicalPathKey(string path)
    {
        return path.Trim().Replace('\\', '/').TrimStart('/');
    }

    private static void EvictExpired()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
        foreach (var pair in IdempotencyCache)
        {
            if (pair.Value.SavedAt < cutoff)
            {
                IdempotencyCache.TryRemove(pair.Key, out _);
            }
        }
    }
}
