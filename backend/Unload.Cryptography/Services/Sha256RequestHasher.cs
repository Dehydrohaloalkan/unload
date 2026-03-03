using System.Security.Cryptography;
using System.Text;
using Unload.Core;

namespace Unload.Cryptography;

/// <summary>
/// Реализация хеширования строк через SHA-256.
/// Используется для получения детерминированного hex-идентификатора из входного значения.
/// </summary>
public class Sha256RequestHasher : IRequestHasher
{
    /// <summary>
    /// Вычисляет SHA-256 хеш входной строки.
    /// </summary>
    /// <param name="value">Строка для хеширования.</param>
    /// <returns>Hex-строка хеша в нижнем регистре.</returns>
    public string ComputeHash(string value)
    {
        var input = Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
