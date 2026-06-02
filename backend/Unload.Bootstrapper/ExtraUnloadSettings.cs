namespace Unload.Bootstrapper;

/// <summary>
/// Настройки extra-выгрузки из секции <c>Extra</c> appsettings.
/// Раньше эти параметры были зашиты в коде (<c>ExtraUnloadOptions</c>); вынесены в конфиг,
/// чтобы их можно было менять без пересборки.
/// </summary>
/// <param name="ChunkSizeBytes">Максимальный размер файла-чанка в байтах.</param>
/// <param name="ScriptsFolderName">Имя подпапки базовых extra-скриптов внутри <c>scripts</c>.</param>
/// <param name="AtomicFolderName">Имя подпапки atomic-скриптов внутри папки extra-скриптов.</param>
/// <param name="BanksScriptName">Имя файла-скрипта справочника банков.</param>
public sealed record ExtraUnloadSettings(
    int ChunkSizeBytes,
    string ScriptsFolderName,
    string AtomicFolderName,
    string BanksScriptName)
{
    public const string SectionName = "Extra";

    public static readonly ExtraUnloadSettings Default = new(
        ChunkSizeBytes: 10 * 1024 * 1024,
        ScriptsFolderName: "extra",
        AtomicFolderName: "atomic",
        BanksScriptName: "_banks.sql");
}
