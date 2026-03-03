using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unload.Console.CatalogSelection;

internal static class CatalogSelectionLoader
{
    public static async Task<IReadOnlyList<CatalogSelectionGroup>> LoadAsync(string catalogPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(catalogPath);
        var root = await JsonSerializer.DeserializeAsync(
            stream,
            CatalogSelectionJsonContext.Default.SelectionCatalogRoot,
            cancellationToken);

        if (root is null)
        {
            throw new InvalidOperationException("Catalog json is empty.");
        }

        var membersById = root.Members
            .GroupBy(static x => x.Id)
            .ToDictionary(static x => x.Key, static x => x.First());

        var groupedProfiles = root.Groups
            .Select(groupData =>
            {
                var profiles = membersById.Values
                    .Where(member => member.Groups.Contains(groupData.Id))
                    .Select(member =>
                    {
                        var normalizedFolder = groupData.Folder.Trim().ToUpperInvariant();
                        var normalizedCode = member.Code.Trim().ToUpperInvariant();
                        var profileCode = $"{normalizedFolder}_{normalizedCode}";

                        return new CatalogSelectionProfile(profileCode, member.Name, normalizedCode);
                    })
                    .DistinctBy(static x => x.ProfileCode, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static x => x.MemberName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new CatalogSelectionGroup(groupData.Id, groupData.Name, profiles);
            })
            .OrderBy(static x => x.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return groupedProfiles;
    }
}

internal record SelectionCatalogRoot(
    [property: JsonPropertyName("groups")] List<SelectionGroup> Groups,
    [property: JsonPropertyName("members")] List<SelectionMember> Members);

internal record SelectionGroup(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("folder")] string Folder);

internal record SelectionMember(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("groups")] List<int> Groups);

[JsonSerializable(typeof(SelectionCatalogRoot))]
internal partial class CatalogSelectionJsonContext : JsonSerializerContext;
