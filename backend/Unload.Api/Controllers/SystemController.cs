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
    IMqSenderFeedbackConsumer mqSenderFeedbackConsumer) : ControllerBase
{
    private readonly IGetServerTimeUseCase _getServerTimeUseCase = getServerTimeUseCase;
    private readonly UnloadRuntimePaths _runtimePaths = runtimePaths;
    private readonly IOutputFilesService _outputFilesService = outputFilesService;
    private readonly IMqSenderFeedbackConsumer _mqSenderFeedbackConsumer = mqSenderFeedbackConsumer;

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
