using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Unload.Core;

namespace Unload.Gateway;

public class GatewayUploadService(
    GatewayUploadOptions options,
    IGatewayPublisher gatewayPublisher,
    ILogger<GatewayUploadService> logger)
{
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

        if (files.Count > 20)
        {
            throw new ArgumentException("Too many files (max 20).");
        }

        var safeMemberName = string.IsNullOrWhiteSpace(memberName) ? "manual" : memberName.Trim();
        var requestId = $"upload-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..43];
        var correlationId = requestId;
        var batchId = $"batch-{Guid.NewGuid():N}"[..24];

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

            if (formFile.Length > 100 * 1024 * 1024)
            {
                failed++;
                results.Add(new GatewayUploadFileResult(
                    FileName: formFile.FileName ?? "unknown",
                    SizeBytes: formFile.Length,
                    Status: "failed",
                    Message: "File too large (max 100MB)."));
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
}
