using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;
using Unload.Core;
using Unload.Api.ErrorHandling;
using Unload.Api.UseCases;
using Unload.Bootstrapper;

namespace Unload.Api.Controllers;

/// <summary>
/// Служебные transport-endpoint'ы API.
/// </summary>
[ApiController]
[Route("api/system")]
public  class SystemController(
    IGetServerTimeUseCase getServerTimeUseCase,
    UnloadRuntimePaths runtimePaths,
    IMqSenderFeedbackConsumer mqSenderFeedbackConsumer) : ControllerBase
{
    private readonly IGetServerTimeUseCase _getServerTimeUseCase = getServerTimeUseCase;
    private readonly UnloadRuntimePaths _runtimePaths = runtimePaths;
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

        var outputRoot = Path.GetFullPath(_runtimePaths.OutputDirectory);
        var fullPath = ResolveDownloadPath(outputRoot, path);
        var outputRootPrefix = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(outputRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Requested file must be located inside the output directory.",
                "INVALID_DOWNLOAD_PATH");
        }

        if (!System.IO.File.Exists(fullPath))
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                "File was not found",
                "Requested output file was not found.",
                "OUTPUT_FILE_NOT_FOUND");
        }

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return File(stream, "application/octet-stream", Path.GetFileName(fullPath), enableRangeProcessing: true);
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

        var outputRoot = Path.GetFullPath(_runtimePaths.OutputDirectory);
        var fullPath = ResolveDownloadPath(outputRoot, path);
        var outputRootPrefix = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(outputRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Requested path must be located inside the output directory.",
                "INVALID_OUTPUT_PATH");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                "Directory was not found",
                "Requested output directory was not found.",
                "OUTPUT_DIRECTORY_NOT_FOUND");
        }

        var files = Directory
            .EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
            .Select(file =>
            {
                var info = new FileInfo(file);
                return new OutputFileInfo(
                    Path.GetFileName(file),
                    file,
                    info.LastWriteTimeUtc,
                    info.Length);
            })
            .OrderByDescending(static file => file.ModifiedAt)
            .ToArray();
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

        var outputRoot = Path.GetFullPath(_runtimePaths.OutputDirectory);
        var fullPath = ResolveDownloadPath(outputRoot, path);
        var outputRootPrefix = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(outputRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                "Requested path must be located inside the output directory.",
                "INVALID_OUTPUT_PATH");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                "Directory was not found",
                "Requested output directory was not found.",
                "OUTPUT_DIRECTORY_NOT_FOUND");
        }

        var zipFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip");
        using (var archiveStream = new FileStream(zipFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(fullPath, file);
                archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
            }
        }

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

        var downloadName = $"{Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}.zip";
        var stream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, "application/zip", downloadName, enableRangeProcessing: true);
    }

    private static string ResolveDownloadPath(string outputRoot, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(outputRoot, path));
    }
}
