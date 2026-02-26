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

        var groupsById = root.Groups
            .GroupBy(static x => x.Id)
            .ToDictionary(static x => x.Key, static x => x.First().Name);

        var groupedProfiles = root.Profiles
            .GroupBy(static x => x.GroupId)
            .Select(group =>
            {
                var groupName = groupsById.TryGetValue(group.Key, out var found) ? found : $"Group {group.Key}";
                var codes = group
                    .Select(static x => x.Code.Trim().ToUpperInvariant())
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new CatalogSelectionGroup(group.Key, groupName, codes);
            })
            .OrderBy(static x => x.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return groupedProfiles;
    }
}

internal record SelectionCatalogRoot(
    [property: JsonPropertyName("groups")] List<SelectionGroup> Groups,
    [property: JsonPropertyName("profiles")] List<SelectionProfile> Profiles);

internal record SelectionGroup(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

internal record SelectionProfile(
    [property: JsonPropertyName("group_id")] int GroupId,
    [property: JsonPropertyName("code")] string Code);

[JsonSerializable(typeof(SelectionCatalogRoot))]
internal partial class CatalogSelectionJsonContext : JsonSerializerContext;
