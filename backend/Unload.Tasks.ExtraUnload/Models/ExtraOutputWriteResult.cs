using Unload.Core;

namespace Unload.Tasks.ExtraUnload;

public record ExtraOutputWriteResult(string OutputPath, int FilesWritten);

/// <summary>
/// Результат записи файлов одного extra-скрипта.
/// </summary>
/// <param name="FilesWritten">Сколько файлов записано.</param>
/// <param name="Files">Дескрипторы записанных файлов (для эмита событий и публикации в шлюз).</param>
public record ExtraScriptWriteResult(
    int FilesWritten,
    IReadOnlyList<SenderFileDescriptor> Files);
