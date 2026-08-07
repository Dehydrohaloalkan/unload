using System.Text;

namespace Unload.Core;

/// <summary>
/// Единая кодировка всех текстовых артефактов выгрузки.
/// </summary>
public static class OutputTextEncoding
{
    /// <summary>
    /// Windows-1251 без BOM. Непредставимые символы заменяются стандартным знаком вопроса.
    /// </summary>
    public static readonly Encoding Windows1251 = CreateWindows1251();

    private static Encoding CreateWindows1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251);
    }
}
