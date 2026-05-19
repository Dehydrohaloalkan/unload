using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.Gateway;

public class GatewayUploadService(
    GatewayUploadOptions options,
    IGatewayPublisher gatewayPublisher,
    ILogger<GatewayUploadService> logger)
{
    /// <summary>Максимум файлов в одной загрузке.</summary>
    private const int MaxFileCount = 20;

    /// <summary>Максимальный размер одного файла (100 МБ).</summary>
    private const long MaxFileSizeBytes = 100L * 1024 * 1024;

    /// <summary>Длина усечённого GUID-токена в request-id и batch-id.</summary>
    private const int IdTokenLength = 18;

    private readonly GatewayUploadOptions _options = options;
    private readonly IGatewayPublisher _gatewayPublisher = gatewayPublisher;
    private readonly ILogger<GatewayUploadService> _logger = logger;

    public async Task<GatewayUploadResponse> UploadAsync(
        IReadOnlyList<IFormFile> files,
        string? memberName,
        CancellationToken cancellationToken)
    {
        if (files is null || files.Count == 0)
        {
            throw new ArgumentException("At least one file is required.");
        }

        if (files.Count > MaxFileCount)
        {
            throw new ArgumentException($"Too many files (max {MaxFileCount}).");
        }

        var safeMemberName = string.IsNullOrWhiteSpace(memberName) ? "manual" : memberName.Trim();
        var requestId = $"upload-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid().ToString("N")[..IdTokenLength]}";
        var correlationId = requestId;
        var batchId = $"batch-{Guid.NewGuid().ToString("N")[..IdTokenLength]}";

        var uploadRoot = Path.Combine(_options.UploadRootDirectory, requestId);
        Directory.CreateDirectory(uploadRoot);

        var results = new List<GatewayUploadFileResult>(files.Count);
        var descriptors = new List<SenderFileDescriptor>(files.Count);
        var accepted = 0;
        var failed = 0;

        foreach (var formFile in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (formFile is null || formFile.Length <= 0)
            {
                failed++;
                results.Add(new GatewayUploadFileResult(
                    FileName: formFile?.FileName ?? "unknown",
                    SizeBytes: formFile?.Length ?? 0,
                    Status: "failed",
                    Message: "Empty file."));
                continue;
            }

            if (formFile.Length > MaxFileSizeBytes)
            {
                failed++;
                results.Add(new GatewayUploadFileResult(
                    FileName: formFile.FileName ?? "unknown",
                    SizeBytes: formFile.Length,
                    Status: "failed",
                    Message: $"File too large (max {MaxFileSizeBytes / (1024 * 1024)}MB)."));
                continue;
            }

            var safeName = Path.GetFileName(formFile.FileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(safeName))
            {
                failed++;
                results.Add(new GatewayUploadFileResult(
                    FileName: formFile.FileName ?? "unknown",
                    SizeBytes: formFile.Length,
                    Status: "failed",
                    Message: "Invalid file name."));
                continue;
            }

            var targetPath = Path.Combine(uploadRoot, safeName);
            try
            {
                await using (var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await formFile.CopyToAsync(stream, cancellationToken);
                }

                var info = new FileInfo(targetPath);
                descriptors.Add(new SenderFileDescriptor(
                    FilePath: info.FullName,
                    FileName: info.Name,
                    SizeBytes: info.Length));
                accepted++;
                results.Add(new GatewayUploadFileResult(
                    FileName: info.Name,
                    SizeBytes: info.Length,
                    Status: "accepted"));
            }
            catch (Exception ex)
            {
                failed++;
                results.Add(new GatewayUploadFileResult(
                    FileName: safeName,
                    SizeBytes: formFile.Length,
                    Status: "failed",
                    Message: ex.Message));
            }
        }

        if (descriptors.Count > 0)
        {
            var evt = new SenderFileBatchReadyEvent(
                OccurredAt: DateTimeOffset.UtcNow,
                CorrelationId: correlationId,
                MemberName: safeMemberName,
                BatchId: batchId,
                Version: 1,
                Files: descriptors);

            await _gatewayPublisher.PublishFileBatchReadyAsync(evt, cancellationToken);
            _logger.LogInformation(
                "Gateway upload published. RequestId: {RequestId}, Files: {Files}, BatchId: {BatchId}",
                requestId,
                descriptors.Count,
                batchId);
        }

        return new GatewayUploadResponse(
            RequestId: requestId,
            CorrelationId: correlationId,
            MemberName: safeMemberName,
            BatchId: batchId,
            AcceptedFiles: accepted,
            FailedFiles: failed,
            Files: results);
    }

    /// <summary>
    /// Удаляет staging-каталоги загрузок старше дня хранения.
    /// Каждая загрузка создаёт собственный каталог; без этой чистки они растут бесконечно.
    /// Вызывается фоновой ретеншн-задачей по тому же расписанию, что и чистка истории.
    /// </summary>
    /// <param name="oldestDayToKeepInclusive">Самый ранний день, который ещё сохраняется.</param>
    /// <returns>Количество удалённых каталогов.</returns>
    public int PruneStagingDirectories(DateOnly oldestDayToKeepInclusive)
    {
        if (!Directory.Exists(_options.UploadRootDirectory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var directory in Directory.EnumerateDirectories(_options.UploadRootDirectory))
        {
            try
            {
                var lastWriteDay = DateOnly.FromDateTime(Directory.GetLastWriteTime(directory));
                if (lastWriteDay >= oldestDayToKeepInclusive)
                {
                    continue;
                }

                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to prune gateway upload staging directory '{Directory}'.", directory);
            }
        }

        return removed;
    }
}
