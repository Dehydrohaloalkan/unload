namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Пути к директориям, необходимым для extra-выгрузки.
/// </summary>
public record ExtraUnloadOptions(string ScriptsDirectory, string OutputDirectory);
