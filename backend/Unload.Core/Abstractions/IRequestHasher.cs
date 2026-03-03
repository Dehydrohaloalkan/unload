namespace Unload.Core;

/// <summary>
/// Контракт вычисления хеша для строковых значений запроса.
/// Используется для формирования устойчивых идентификаторов и контрольных значений.
/// </summary>
public interface IRequestHasher
{
    /// <summary>
    /// Вычисляет хеш входной строки.
    /// </summary>
    /// <param name="value">Исходное строковое значение.</param>
    /// <returns>Строковое представление хеша.</returns>
    string ComputeHash(string value);
}
