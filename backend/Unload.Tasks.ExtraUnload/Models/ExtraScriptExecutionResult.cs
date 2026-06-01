namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Результат выполнения одного extra-скрипта: строки <c>LineFile</c>, сгруппированные по банку.
/// </summary>
/// <param name="ScriptCode">Код скрипта (имя файла без расширения).</param>
/// <param name="LinesByBank">Строки данных по банкам (ключ — значение NrBank).</param>
/// <param name="Records">Общее число прочитанных строк.</param>
public record ExtraScriptExecutionResult(
    string ScriptCode,
    IReadOnlyDictionary<string, List<string>> LinesByBank,
    int Records);
