namespace Unload.Core;

/// <summary>
/// Описывает SQL-скрипт, выбранный из каталога для конкретного профиля.
/// Используется раннером при выполнении запроса и формировании имени выходного файла.
/// </summary>
/// <param name="ProfileCode">Код профиля, к которому относится скрипт.</param>
/// <param name="ScriptCode">Код скрипта (обычно имя SQL-файла без расширения).</param>
/// <param name="OutputFileStem">Базовая часть имени файла результата без расширения и суффикса чанка.</param>
/// <param name="OutputFileExtension">Расширение выходного файла, например <c>.txt</c>.</param>
/// <param name="ScriptPath">Полный путь к исходному SQL-файлу.</param>
/// <param name="SqlText">Текст SQL-запроса, выполняемый в БД.</param>
public record ScriptDefinition(
    string ProfileCode,
    string ScriptCode,
    string OutputFileStem,
    string OutputFileExtension,
    string ScriptPath,
    string SqlText);
