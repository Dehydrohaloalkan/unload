namespace Unload.Console.CatalogSelection;

internal record CatalogSelectionGroup(
    int GroupId,
    string GroupName,
    IReadOnlyList<CatalogSelectionProfile> Profiles);

internal record CatalogSelectionProfile(
    string ProfileCode,
    string MemberName,
    string MemberCode);
