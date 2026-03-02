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

        var groupsById = root.Groups
            .GroupBy(static x => x.Id)
            .ToDictionary(static x => x.Key, static x => x.First());

        var groupedProfiles = root.Profiles
            .GroupBy(static x => x.GroupId)
            .Select(group =>
            {
                if (!groupsById.TryGetValue(group.Key, out var groupData))
                {
                    throw new InvalidOperationException($"Group '{group.Key}' not found in catalog.");
                }

                var profiles = group
                    .Select(profile =>
                    {
                        if (!membersById.TryGetValue(profile.MemberId, out var member))
                        {
                            throw new InvalidOperationException(
                                $"Member '{profile.MemberId}' not found in catalog.");
                        }

                        var normalizedFolder = groupData.Folder.Trim().ToUpperInvariant();
                        var normalizedCode = member.Code.Trim().ToUpperInvariant();
                        var profileCode = $"{normalizedFolder}_{normalizedCode}";

                        return new CatalogSelectionProfile(profileCode, member.Name, normalizedCode);
                    })
                    .DistinctBy(static x => x.ProfileCode, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static x => x.MemberName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new CatalogSelectionGroup(group.Key, groupData.Name, profiles);
            })
            .OrderBy(static x => x.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return groupedProfiles;
    }
}

internal record SelectionCatalogRoot(
    [property: JsonPropertyName("groups")] List<SelectionGroup> Groups,
    [property: JsonPropertyName("members")] List<SelectionMember> Members,
    [property: JsonPropertyName("profiles")] List<SelectionProfile> Profiles);

internal record SelectionGroup(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("folder")] string Folder);

internal record SelectionMember(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("code")] string Code);

internal record SelectionProfile(
    [property: JsonPropertyName("group_id")] int GroupId,
    [property: JsonPropertyName("member_id")] int MemberId);

[JsonSerializable(typeof(SelectionCatalogRoot))]
internal partial class CatalogSelectionJsonContext : JsonSerializerContext;
