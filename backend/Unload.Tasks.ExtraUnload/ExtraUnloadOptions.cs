namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Пути к директориям и параметры extra-выгрузки.
/// Имена подпапок и размер чанка читаются из конфигурации (секция <c>Extra</c>),
/// а не зашиты в коде — см. <c>ExtraUnloadSettings</c> в Bootstrapper.
/// </summary>
/// <param name="ScriptsDirectory">Корень каталога SQL-скриптов (внутри ожидается подпапка extra).</param>
/// <param name="OutputDirectory">Базовая директория для выходных файлов.</param>
/// <param name="ChunkSizeBytes">Максимальный размер файла-чанка в байтах.</param>
/// <param name="ExtraFolderName">Имя подпапки базовых extra-скриптов.</param>
/// <param name="AtomicFolderName">Имя подпапки atomic-скриптов внутри <paramref name="ExtraFolderName"/>.</param>
/// <param name="BanksScriptName">Имя файла-скрипта справочника банков.</param>
public record ExtraUnloadOptions(
    string ScriptsDirectory,
    string OutputDirectory,
    int ChunkSizeBytes = 10 * 1024 * 1024,
    string ExtraFolderName = "extra",
    string AtomicFolderName = "atomic",
    string BanksScriptName = "_banks.sql")
{
    /// <summary>Каталог базовых extra-скриптов (выполняются, когда выбраны все банки).</summary>
    public string ExtraScriptsDirectory => Path.Combine(ScriptsDirectory, ExtraFolderName);

    /// <summary>Каталог atomic-скриптов с плейсхолдером <c>{banks}</c> (выполняются при выборе подмножества банков).</summary>
    public string AtomicScriptsDirectory => Path.Combine(ScriptsDirectory, ExtraFolderName, AtomicFolderName);

    /// <summary>Путь к зарезервированному скрипту справочника банков (NrBank + BankName).</summary>
    public string BanksScriptPath => Path.Combine(ScriptsDirectory, ExtraFolderName, BanksScriptName);
}
