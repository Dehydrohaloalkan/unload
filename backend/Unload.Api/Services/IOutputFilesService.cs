using Unload.Api;

namespace Unload.Api;

public interface IOutputFilesService
{
    FileStream OpenOutputFile(string path);

    OutputFileInfo[] ListOutputFiles(string path);

    OutputArchiveInfo CreateOutputArchive(string path);
}

public record OutputArchiveInfo(
    string ZipFilePath,
    string DownloadName);

