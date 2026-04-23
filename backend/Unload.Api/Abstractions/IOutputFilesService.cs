using Unload.Api.Models;

namespace Unload.Api.Abstractions;

public interface IOutputFilesService
{
    FileStream OpenOutputFile(string path);

    OutputFileInfo[] ListOutputFiles(string path);

    OutputArchiveInfo CreateOutputArchive(string path);
}

