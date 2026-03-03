using System.Text.RegularExpressions;

namespace Unload.Catalog;

/// <summary>
/// Вспомогательные операции для отбора скриптов и построения имен выгрузки.
/// </summary>
internal static class CatalogScriptPathHelper
{
    private static readonly Regex ScriptNamePattern = new(
        "^Y(?<member>[A-Z0-9])(?<group>[A-Z0-9])_(?<type>[A-Z0-9]+)_(?<codes>[0-9]+(?:_[0-9]+)*)_(?<ext>[A-Z0-9]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ScriptNameParts ParseScriptName(string scriptCode)
    {
        var normalized = scriptCode.Trim().ToUpperInvariant();
        var match = ScriptNamePattern.Match(normalized);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Script name '{scriptCode}' does not match required format " +
                "'Y<member><group>_<type>_<codes>_<extension>'.");
        }

        var memberCode = match.Groups["member"].Value;
        var groupCode = match.Groups["group"].Value;
        var scriptType = match.Groups["type"].Value;
        var scriptCodes = match.Groups["codes"].Value;
        var outputExtensionWithoutDot = match.Groups["ext"].Value;
        var firstCodeDigit = GetFirstCodeDigit(scriptCodes, scriptCode);

        return new ScriptNameParts(
            Prefix: normalized[..3],
            MemberCode: memberCode,
            GroupCode: groupCode,
            ScriptType: scriptType,
            ScriptCodes: scriptCodes,
            OutputExtensionWithoutDot: outputExtensionWithoutDot,
            FirstCodeDigit: firstCodeDigit);
    }

    public static bool IsScriptForTarget(
        string scriptPath,
        string expectedMemberCode,
        string expectedGroupCode,
        string expectedFileExtension)
    {
        var fileName = Path.GetFileNameWithoutExtension(scriptPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        ScriptNameParts parsed;
        try
        {
            parsed = ParseScriptName(fileName);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var normalizedMemberCode = expectedMemberCode.Trim().ToUpperInvariant();
        var normalizedGroupCode = expectedGroupCode.Trim().ToUpperInvariant();
        var normalizedExtension = expectedFileExtension.Trim().TrimStart('.').ToUpperInvariant();

        return parsed.MemberCode == normalizedMemberCode
            && parsed.GroupCode == normalizedGroupCode
            && parsed.OutputExtensionWithoutDot == normalizedExtension;
    }

    public static int GetScriptOrder(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return int.MaxValue;
        }

        try
        {
            var parsed = ParseScriptName(fileName);
            return int.Parse(parsed.ScriptCodes.Split('_', StringSplitOptions.RemoveEmptyEntries)[0]);
        }
        catch (Exception)
        {
            return int.MaxValue;
        }
    }

    public static string BuildTargetCode(string groupFolder, string memberCode)
    {
        return $"{groupFolder}_{memberCode}";
    }

    private static int GetFirstCodeDigit(string scriptCodes, string scriptCode)
    {
        var firstCode = scriptCodes.Split('_', StringSplitOptions.RemoveEmptyEntries)[0];
        if (firstCode.Length == 0 || !char.IsDigit(firstCode[0]))
        {
            throw new InvalidOperationException(
                $"Script '{scriptCode}' contains invalid codes segment '{scriptCodes}'.");
        }

        return firstCode[0] - '0';
    }
}

internal readonly record struct ScriptNameParts(
    string Prefix,
    string MemberCode,
    string GroupCode,
    string ScriptType,
    string ScriptCodes,
    string OutputExtensionWithoutDot,
    int FirstCodeDigit);
