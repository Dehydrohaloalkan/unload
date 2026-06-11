using FluentFTP;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Unload.Core;

namespace Unload.Gateway;

/// <summary>
/// Фоновый сервис шлюза: читает batch-ready события и отправляет файлы по FTP.
/// Алгоритм: загружает все файлы в staging-директорию (обработчик её не смотрит),
/// затем атомарно переименовывает каждый файл в целевую директорию в отсортированном порядке.
/// RNFR/RNTO на NTFS в пределах одного тома — мгновенная операция,
/// поэтому обработчик видит только полностью записанные файлы.
/// </summary>
public class FtpGatewayBackgroundService(
    IGatewayBatchSource batchSource,
    IGatewayPublisher gatewayPublisher,
    IOptions<GatewayOptions> options,
    ILogger<FtpGatewayBackgroundService> logger) : BackgroundService
{
    private readonly IGatewayBatchSource _batchSource = batchSource;
    private readonly IGatewayPublisher _gatewayPublisher = gatewayPublisher;
    private readonly GatewayOptions _options = options.Value;
    private readonly ILogger<FtpGatewayBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var batch in _batchSource.ReadBatchReadyEventsAsync(stoppingToken))
        {
            await ProcessBatchAsync(batch, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(SenderFileBatchReadyEvent batch, CancellationToken cancellationToken)
    {
        var memberName = (batch.MemberName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(memberName))
            return;

        try
        {
            _logger.LogInformation(
                "Gateway sender started batch. CorrelationId: {CorrelationId}, Member: {MemberName}, Files: {FilesCount}",
                batch.CorrelationId, memberName, batch.Files.Count);

            var sortedFiles = batch.Files
                .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            using var ftp = CreateFtpClient();
            await ftp.Connect(cancellationToken);

            var targetDir  = _options.Ftp.RemoteDirectory.TrimEnd('/');
            var stagingDir = ResolveStagingDir(targetDir);

            // Phase 1: upload all files fully into staging (handler does not watch staging)
            foreach (var file in sortedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stagingPath = $"{stagingDir}/{file.FileName}";
                await UploadFileAsync(ftp, file.FilePath, stagingPath, cancellationToken);
                _logger.LogDebug("Uploaded to staging: {FileName} → {StagingPath}", file.FileName, stagingPath);
            }

            // Phase 2: atomic rename staging → target in sorted order
            // On NTFS (same volume) RNFR/RNTO is instantaneous — handler sees only complete files
            foreach (var file in sortedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stagingPath = $"{stagingDir}/{file.FileName}";
                var targetPath  = $"{targetDir}/{file.FileName}";
                await ftp.Rename(stagingPath, targetPath, cancellationToken);
                _logger.LogInformation(
                    "File published: {FileName} (CorrelationId: {CorrelationId})",
                    file.FileName, batch.CorrelationId);

                await _gatewayPublisher.PublishSenderFeedbackAsync(
                    new SenderFileDispatchFeedback(
                        OccurredAt: DateTimeOffset.UtcNow,
                        CorrelationId: batch.CorrelationId,
                        MemberName: memberName,
                        BatchId: batch.BatchId,
                        Kind: SenderFeedbackKind.FileSent,
                        FilePath: file.FilePath,
                        Message: $"File sent: {file.FileName}"),
                    cancellationToken);
            }

            await _gatewayPublisher.PublishSenderFeedbackAsync(
                new SenderFileDispatchFeedback(
                    OccurredAt: DateTimeOffset.UtcNow,
                    CorrelationId: batch.CorrelationId,
                    MemberName: memberName,
                    BatchId: batch.BatchId,
                    Kind: SenderFeedbackKind.BatchCompleted,
                    Message: $"Batch completed. Files sent: {sortedFiles.Length}."),
                cancellationToken);

            _logger.LogInformation(
                "Gateway sender completed batch. CorrelationId: {CorrelationId}, Member: {MemberName}",
                batch.CorrelationId, memberName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PublishSafeAsync(new SenderFileDispatchFeedback(
                OccurredAt: DateTimeOffset.UtcNow,
                CorrelationId: batch.CorrelationId,
                MemberName: memberName,
                BatchId: batch.BatchId,
                Kind: SenderFeedbackKind.BatchFailed,
                Message: "Gateway sender was cancelled."));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Gateway sender failed batch. CorrelationId: {CorrelationId}, Member: {MemberName}",
                batch.CorrelationId, memberName);

            await PublishSafeAsync(new SenderFileDispatchFeedback(
                OccurredAt: DateTimeOffset.UtcNow,
                CorrelationId: batch.CorrelationId,
                MemberName: memberName,
                BatchId: batch.BatchId,
                Kind: SenderFeedbackKind.BatchFailed,
                Message: ex.Message));
        }
    }

    private string ResolveStagingDir(string targetDir)
    {
        if (!string.IsNullOrWhiteSpace(_options.Ftp.StagingDirectory))
            return _options.Ftp.StagingDirectory.TrimEnd('/');

        // Auto-derive: sibling directory named "staging" next to targetDir
        int lastSlash = targetDir.LastIndexOf('/');
        string parent = lastSlash > 0 ? targetDir[..lastSlash] : string.Empty;
        return $"{parent}/staging";
    }

    private static async Task UploadFileAsync(
        AsyncFtpClient ftp,
        string localPath,
        string remotePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await ftp.UploadStream(stream, remotePath, FtpRemoteExists.Overwrite, createRemoteDir: true, token: cancellationToken);
    }

    private AsyncFtpClient CreateFtpClient()
    {
        var cfg = _options.Ftp;
        return new AsyncFtpClient(
            cfg.Host,
            cfg.Username,
            cfg.Password,
            cfg.Port,
            new FtpConfig
            {
                ConnectTimeout = cfg.ConnectTimeoutMs,
                ReadTimeout = cfg.ConnectTimeoutMs,
                DataConnectionReadTimeout = cfg.ConnectTimeoutMs
            });
    }

    private async Task PublishSafeAsync(SenderFileDispatchFeedback feedback)
    {
        try
        {
            await _gatewayPublisher.PublishSenderFeedbackAsync(feedback, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish gateway feedback. CorrelationId: {CorrelationId}, BatchId: {BatchId}, Kind: {Kind}",
                feedback.CorrelationId, feedback.BatchId, feedback.Kind);
        }
    }
}
