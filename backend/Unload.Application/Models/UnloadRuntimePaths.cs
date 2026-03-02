namespace Unload.Application;

public record UnloadRuntimePaths(
    string CatalogPath,
    string ScriptsDirectory,
    string OutputDirectory,
    string DiagnosticsDirectory);
