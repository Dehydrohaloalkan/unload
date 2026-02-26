namespace Unload.Console.CatalogSelection;

internal record CatalogSelectionGroup(
    int GroupId,
    string GroupName,
    IReadOnlyList<string> ProfileCodes);
