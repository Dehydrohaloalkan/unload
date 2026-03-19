using Microsoft.AspNetCore.Mvc;
using Unload.Api.ErrorHandling;
using Unload.Api.UseCases;
using Unload.Bootstrapper;

namespace Unload.Api.Controllers;

/// <summary>
/// Служебные transport-endpoint'ы API.
/// </summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    private readonly IGetServerTimeUseCase _getServerTimeUseCase;
    private readonly UnloadRuntimePaths _runtimePaths;

    public SystemController(
        IGetServerTimeUseCase getServerTimeUseCase,
        UnloadRuntimePaths runtimePaths)
    {
        _getServerTimeUseCase = getServerTimeUseCase;
        _runtimePaths = runtimePaths;
    }

    /// <summary>
    /// Возвращает текущее локальное и UTC-время сервера.
    /// Используется UI-клиентами для синхронизации часов с backend.
    /// </summary>
    [HttpGet("time")]
    public IActionResult GetServerTime()
    {
        return Ok(_getServerTimeUseCase.Execute());
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

    private static string ResolveDownloadPath(string outputRoot, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(outputRoot, path));
    }
}
