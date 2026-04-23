using System.IO.Compression;
using Unload.Api.ErrorHandling;
using Unload.Bootstrapper;

namespace Unload.Api;

public class OutputFilesService(UnloadRuntimePaths runtimePaths) : IOutputFilesService
{
    private readonly UnloadRuntimePaths _runtimePaths = runtimePaths;

    public FileStream OpenOutputFile(string path)
    {
        var outputRoot = Path.GetFullPath(_runtimePaths.OutputDirectory);
        var fullPath = ResolveDownloadPath(outputRoot, path);
        EnsureInsideOutputRoot(
            outputRoot,
            fullPath,
            invalidPathErrorCode: "INVALID_DOWNLOAD_PATH",
            invalidPathDetail: "Requested file must be located inside the output directory.");

        if (!File.Exists(fullPath))
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                "File was not found",
                "Requested output file was not found.",
                "OUTPUT_FILE_NOT_FOUND");
        }

        return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    public OutputFileInfo[] ListOutputFiles(string path)
    {
        var outputRoot = Path.GetFullPath(_runtimePaths.OutputDirectory);
        var fullPath = ResolveDownloadPath(outputRoot, path);
        EnsureInsideOutputRoot(
            outputRoot,
            fullPath,
            invalidPathErrorCode: "INVALID_OUTPUT_PATH",
            invalidPathDetail: "Requested path must be located inside the output directory.");

        if (!Directory.Exists(fullPath))
        {
            throw new ApiProblemException(
                StatusCodes.Status404NotFound,
                "Directory was not found",
                "Requested output directory was not found.",
                "OUTPUT_DIRECTORY_NOT_FOUND");
        }

        return Directory
            .EnumerateFiles(fullPath, "*", SearchOption.AllDirectories)
            .Select(static file =>
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
    }

    public OutputArchiveInfo CreateOutputArchive(string path)
    {
        var outputRoot = Path.GetFullPath(_runtimePaths.OutputDirectory);
        var fullPath = ResolveDownloadPath(outputRoot, path);
        EnsureInsideOutputRoot(
            outputRoot,
            fullPath,
            invalidPathErrorCode: "INVALID_OUTPUT_PATH",
            invalidPathDetail: "Requested path must be located inside the output directory.");

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

        var downloadName =
            $"{Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}.zip";

        return new OutputArchiveInfo(zipFilePath, downloadName);
    }

    private static void EnsureInsideOutputRoot(
        string outputRoot,
        string fullPath,
        string invalidPathErrorCode,
        string invalidPathDetail)
    {
        var normalizedRoot = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var outputRootPrefix = normalizedRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(outputRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiProblemException(
                StatusCodes.Status400BadRequest,
                "Validation error",
                invalidPathDetail,
                invalidPathErrorCode);
        }
    }

    private static string ResolveDownloadPath(string outputRoot, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(outputRoot, path));
    }
}

