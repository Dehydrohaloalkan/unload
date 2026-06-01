namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Пути к директориям и параметры extra-выгрузки.
/// </summary>
/// <param name="ScriptsDirectory">Корень каталога SQL-скриптов (внутри ожидается подпапка <c>extra</c>).</param>
/// <param name="OutputDirectory">Базовая директория для выходных файлов.</param>
/// <param name="ChunkSizeBytes">Максимальный размер файла-чанка в байтах.</param>
public record ExtraUnloadOptions(
    string ScriptsDirectory,
    string OutputDirectory,
    int ChunkSizeBytes = 10 * 1024 * 1024)
{
    /// <summary>Каталог базовых extra-скриптов (выполняются, когда выбраны все банки).</summary>
    public string ExtraScriptsDirectory => Path.Combine(ScriptsDirectory, "extra");

    /// <summary>Каталог atomic-скриптов с плейсхолдером <c>{banks}</c> (выполняются при выборе подмножества банков).</summary>
    public string AtomicScriptsDirectory => Path.Combine(ScriptsDirectory, "extra", "atomic");

    /// <summary>Путь к зарезервированному скрипту справочника банков (NrBank + BankName).</summary>
    public string BanksScriptPath => Path.Combine(ScriptsDirectory, "extra", "_banks.sql");
}
