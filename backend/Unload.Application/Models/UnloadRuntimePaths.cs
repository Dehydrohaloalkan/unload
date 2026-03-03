namespace Unload.Application;

/// <summary>
/// Набор путей runtime-конфигурации приложения.
/// Используется DI-композицией для инициализации сервисов каталога, runner и диагностики.
/// </summary>
/// <param name="CatalogPath">Путь к файлу каталога профилей.</param>
/// <param name="ScriptsDirectory">Путь к директории SQL-скриптов.</param>
/// <param name="OutputDirectory">Базовая директория для выходных файлов выгрузки.</param>
/// <param name="DiagnosticsDirectory">Базовая директория диагностических CSV-файлов.</param>
public record UnloadRuntimePaths(
    string CatalogPath,
    string ScriptsDirectory,
    string OutputDirectory,
    string DiagnosticsDirectory);
