namespace Unload.Application;

/// <summary>
/// Набор путей runtime-конфигурации приложения.
/// Используется DI-композицией для инициализации сервисов каталога и runner.
/// </summary>
/// <param name="CatalogPath">Путь к файлу каталога target-выборок.</param>
/// <param name="ScriptsDirectory">Путь к директории SQL-скриптов.</param>
/// <param name="OutputDirectory">Базовая директория для выходных файлов выгрузки.</param>
public record UnloadRuntimePaths(
    string CatalogPath,
    string ScriptsDirectory,
    string OutputDirectory);
