namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Элемент справочника банков для выбора в настройках extra-выгрузки.
/// </summary>
/// <param name="NrBank">Код банка (значение колонки NrBank).</param>
/// <param name="BankName">Читаемое имя банка.</param>
public record ExtraBankInfo(string NrBank, string BankName);
