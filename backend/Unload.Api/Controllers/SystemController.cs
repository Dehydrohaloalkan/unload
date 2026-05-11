using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using Unload.Core;
using Unload.Api.ErrorHandling;
using Unload.Api.Abstractions;
using Unload.Api.Models;
using Unload.Api.UseCases.Abstractions;
using Unload.Bootstrapper;

namespace Unload.Api.Controllers;

/// <summary>
/// Служебные transport-endpoint'ы API.
/// </summary>
[ApiController]
[Route("api/system")]
public class SystemController(
    IGetServerTimeUseCase getServerTimeUseCase,
    UnloadRuntimePaths runtimePaths,
    IOutputFilesService outputFilesService,
    IMqSenderFeedbackConsumer mqSenderFeedbackConsumer,
    IMqPublisher mqPublisher,
    ILogger<SystemController> logger) : ControllerBase
{
    private readonly IGetServerTimeUseCase _getServerTimeUseCase = getServerTimeUseCase;
    private readonly UnloadRuntimePaths _runtimePaths = runtimePaths;
    private readonly IOutputFilesService _outputFilesService = outputFilesService;
    private readonly IMqSenderFeedbackConsumer _mqSenderFeedbackConsumer = mqSenderFeedbackConsumer;
    private readonly IMqPublisher _mqPublisher = mqPublisher;
    private readonly ILogger<SystemController> _logger = logger;

    /// <summary>
    /// Возвращает текущее локальное и UTC-время сервера.
    /// Используется UI-клиентами для синхронизации часов с backend.
    /// </summary>
    [HttpGet("time")]
    public IActionResult GetServerTime()
    {
        return Ok(_getServerTimeUseCase.Execute());
    }

    [HttpPost("sender-feedback")]
    public async Task<IActionResult> PostSenderFeedback(
        [FromBody] SenderFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.CorrelationId) ||
            string.IsNullOrWhiteSpace(request.MemberName) ||
            string.IsNullOrWhiteSpace(request.BatchId) ||
            string.IsNullOrWhiteSpace(request.Kind))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Sender feedback payload is invalid.",
                "VALIDATION_ERROR");
        }

        var kind = request.Kind.Trim().ToUpperInvariant() switch
        {
            "FILE_SENT" => SenderFeedbackKind.FileSent,
            "BATCH_COMPLETED" => SenderFeedbackKind.BatchCompleted,
            "BATCH_FAILED" => SenderFeedbackKind.BatchFailed,
            _ => throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                $"Unsupported sender feedback kind '{request.Kind}'.",
                "VALIDATION_ERROR")
        };

        await _mqSenderFeedbackConsumer.ConsumeAsync(
            new SenderFileDispatchFeedback(
                OccurredAt: DateTimeOffset.UtcNow,
                CorrelationId: request.CorrelationId.Trim(),
                MemberName: request.MemberName.Trim(),
                BatchId: request.BatchId.Trim(),
                Kind: kind,
                FilePath: request.FilePath,
                Message: request.Message),
            cancellationToken);

        return Accepted();
    }

    /// <summary>
    /// Загружает один или несколько файлов и публикует их как batch-ready событие в MQ.
    /// </summary>
    [HttpPost("mq-upload")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> UploadFilesToMq(
        [FromForm(Name = "files")] List<IFormFile> files,
        [FromForm(Name = "memberName")] string? memberName,
        CancellationToken cancellationToken)
    {
        if (files is null || files.Count == 0)
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "At least one file is required.",
                "VALIDATION_ERROR");
        }

        if (files.Count > 20)
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Too many files (max 20).",
                "VALIDATION_ERROR");
        }

        var safeMemberName = string.IsNullOrWhiteSpace(memberName) ? "manual" : memberName.Trim();
        var requestId = $"upload-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..43];
        var correlationId = requestId;
        var batchId = $"batch-{Guid.NewGuid():N}"[..24];

        var uploadRoot = Path.Combine(_runtimePaths.OutputDirectory, "_uploads", requestId);
        Directory.CreateDirectory(uploadRoot);

        var results = new List<MqUploadFileResult>(files.Count);
        var descriptors = new List<SenderFileDescriptor>(files.Count);
        var accepted = 0;
        var failed = 0;

        foreach (var formFile in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (formFile is null || formFile.Length <= 0)
            {
                failed++;
                results.Add(new MqUploadFileResult(
                    FileName: formFile?.FileName ?? "unknown",
                    SizeBytes: formFile?.Length ?? 0,
                    Status: "failed",
                    Message: "Empty file."));
                continue;
            }

            if (formFile.Length > 100 * 1024 * 1024)
            {
                failed++;
                results.Add(new MqUploadFileResult(
                    FileName: formFile.FileName,
                    SizeBytes: formFile.Length,
                    Status: "failed",
                    Message: "File too large (max 100MB)."));
                continue;
            }

            var safeName = Path.GetFileName(formFile.FileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(safeName))
            {
                failed++;
                results.Add(new MqUploadFileResult(
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
                results.Add(new MqUploadFileResult(
                    FileName: info.Name,
                    SizeBytes: info.Length,
                    Status: "accepted"));
            }
            catch (Exception ex)
            {
                failed++;
                results.Add(new MqUploadFileResult(
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

            await _mqPublisher.PublishFileBatchReadyAsync(evt, cancellationToken);
            _logger.LogInformation(
                "MQ upload published. RequestId: {RequestId}, Files: {Files}, BatchId: {BatchId}",
                requestId,
                descriptors.Count,
                batchId);
        }

        return Ok(new MqUploadResponse(
            RequestId: requestId,
            CorrelationId: correlationId,
            MemberName: safeMemberName,
            BatchId: batchId,
            AcceptedFiles: accepted,
            FailedFiles: failed,
            Files: results));
    }

    /// <summary>
    /// Отдает файл из директории output для скачивания UI-клиентом.
    /// Путь валидируется и не может выходить за границы runtime output directory.
    /// </summary>
    [HttpGet("download")]
    public IActionResult DownloadOutputFile([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Query parameter 'path' is required.",
                "VALIDATION_ERROR");
        }

        var stream = _outputFilesService.OpenOutputFile(path);
        return File(stream, "application/octet-stream", Path.GetFileName(stream.Name), enableRangeProcessing: true);
    }

    [HttpGet("output-files")]
    public IActionResult ListOutputFiles([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Query parameter 'path' is required.",
                "VALIDATION_ERROR");
        }

        var files = _outputFilesService.ListOutputFiles(path);
        return Ok(files);
    }

    [HttpGet("download-archive")]
    public IActionResult DownloadOutputArchive([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Query parameter 'path' is required.",
                "VALIDATION_ERROR");
        }

        var archiveInfo = _outputFilesService.CreateOutputArchive(path);
        var zipFilePath = archiveInfo.ZipFilePath;

        Response.OnCompleted(() =>
        {
            try
            {
                System.IO.File.Delete(zipFilePath);
            }
            catch
            {
                // ignore cleanup failure
            }

            return Task.CompletedTask;
        });

        var stream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/zip", archiveInfo.DownloadName, enableRangeProcessing: true);
    }
}
