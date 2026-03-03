namespace Unload.Core;

/// <summary>
/// Описывает SQL-скрипт, выбранный из каталога для конкретной target-выборки.
/// Используется раннером при выполнении запроса и формировании имени выходного файла.
/// </summary>
/// <param name="TargetCode">Target-код, к которому относится скрипт.</param>
/// <param name="ScriptCode">Код скрипта (обычно имя SQL-файла без расширения).</param>
/// <param name="OutputFileStem">Префикс имени файла результата (первые 3 символа кода скрипта).</param>
/// <param name="OutputFileExtension">Расширение выходного файла, например <c>.txt</c>.</param>
/// <param name="ScriptType">Тип из имени SQL-скрипта.</param>
/// <param name="ScriptCodes">Цифровой блок кодов из имени SQL-скрипта.</param>
/// <param name="FirstCodeDigit">Первая цифра из блока кодов скрипта.</param>
/// <param name="ScriptPath">Полный путь к исходному SQL-файлу.</param>
/// <param name="SqlText">Текст SQL-запроса, выполняемый в БД.</param>
public record ScriptDefinition(
    string TargetCode,
    string ScriptCode,
    string OutputFileStem,
    string OutputFileExtension,
    string ScriptType,
    string ScriptCodes,
    int FirstCodeDigit,
    string ScriptPath,
    string SqlText);
