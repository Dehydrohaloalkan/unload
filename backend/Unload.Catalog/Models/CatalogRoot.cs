using System.Text.Json.Serialization;

namespace Unload.Catalog;

public record CatalogRoot(
    [property: JsonPropertyName("groups")] List<CatalogGroup> Groups,
    [property: JsonPropertyName("members")] List<CatalogMember> Members,
    [property: JsonPropertyName("profiles")] List<CatalogProfile> Profiles);

public record CatalogGroup(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("folder")] string Folder);

public record CatalogMember(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("code")] string Code);

public record CatalogProfile(
    [property: JsonPropertyName("group_id")] int GroupId,
    [property: JsonPropertyName("member_id")] int MemberId);
