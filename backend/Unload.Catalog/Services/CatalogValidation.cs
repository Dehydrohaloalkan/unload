using System.Text.RegularExpressions;

namespace Unload.Catalog;

/// <summary>
/// Централизованные проверки каталожных кодов и имен.
/// </summary>
internal static class CatalogValidation
{
    private static readonly Regex TargetCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);
    private static readonly Regex GroupFolderPattern = new("^[A-Z0-9_]{3,32}$", RegexOptions.Compiled);
    private static readonly Regex MemberCodePattern = new("^[A-Z0-9]{1,8}$", RegexOptions.Compiled);
    private static readonly Regex MemberFileExtensionPattern = new("^\\.[A-Z0-9]{1,8}$", RegexOptions.Compiled);

    public static void ValidateTargetCode(string targetCode)
    {
        if (!TargetCodePattern.IsMatch(targetCode))
        {
            throw new InvalidOperationException($"Target code '{targetCode}' is invalid.");
        }
    }

    public static void ValidateGroupFolder(string folder)
    {
        var normalized = folder.Trim().ToUpperInvariant();
        if (!GroupFolderPattern.IsMatch(normalized))
        {
            throw new InvalidOperationException($"Group folder '{folder}' is invalid.");
        }
    }

    public static void ValidateMemberCode(string memberCode)
    {
        var normalized = memberCode.Trim().ToUpperInvariant();
        if (!MemberCodePattern.IsMatch(normalized))
        {
            throw new InvalidOperationException($"Member code '{memberCode}' is invalid.");
        }
    }

    public static void ValidateMemberFileExtension(string memberFileExtension)
    {
        var normalized = memberFileExtension.Trim().ToUpperInvariant();
        if (!MemberFileExtensionPattern.IsMatch(normalized))
        {
            throw new InvalidOperationException(
                $"Member file extension '{memberFileExtension}' is invalid.");
        }
    }
}
