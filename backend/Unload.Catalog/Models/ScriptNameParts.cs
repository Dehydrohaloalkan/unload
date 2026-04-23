namespace Unload.Catalog.Models;

internal readonly record struct ScriptNameParts(
    string Prefix,
    string MemberCode,
    string GroupCode,
    string ScriptType,
    string ScriptCodes,
    string OutputExtensionWithoutDot,
    int FirstCodeDigit);

