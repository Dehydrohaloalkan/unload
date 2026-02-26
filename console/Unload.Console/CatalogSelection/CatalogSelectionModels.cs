namespace Unload.Console.CatalogSelection;

internal sealed record CatalogSelectionGroup(
    int GroupId,
    string GroupName,
    IReadOnlyList<string> ProfileCodes);
