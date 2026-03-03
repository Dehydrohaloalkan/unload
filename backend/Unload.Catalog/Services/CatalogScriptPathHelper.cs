namespace Unload.Catalog;

/// <summary>
/// Вспомогательные операции для отбора скриптов и построения имен выгрузки.
/// </summary>
internal static class CatalogScriptPathHelper
{
    public static bool IsScriptForMember(string scriptPath, string memberCode)
    {
        var fileName = Path.GetFileNameWithoutExtension(scriptPath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length < 2)
        {
            return false;
        }

        return char.ToUpperInvariant(fileName[1]) == char.ToUpperInvariant(memberCode[0]);
    }

    public static int GetScriptOrder(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var lastUnderscore = fileName.LastIndexOf('_');
        if (lastUnderscore < 0)
        {
            return int.MaxValue;
        }

        var suffix = fileName[(lastUnderscore + 1)..];
        return int.TryParse(suffix, out var number) ? number : int.MaxValue;
    }

    public static string BuildTargetCode(string groupFolder, string memberCode)
    {
        return $"{groupFolder}_{memberCode}";
    }

    public static string BuildOutputFileStem(string memberCode, string groupFolder, string scriptCode)
    {
        if (scriptCode.Length < 3)
        {
            throw new InvalidOperationException(
                $"Script '{scriptCode}' must have at least 3 characters in file name.");
        }

        var tail = scriptCode[3..].TrimStart('_');
        var groupThirdLetter = groupFolder[2];
        return string.IsNullOrWhiteSpace(tail)
            ? $"Y{memberCode}{groupThirdLetter}"
            : $"Y{memberCode}{groupThirdLetter}_{tail}";
    }
}
