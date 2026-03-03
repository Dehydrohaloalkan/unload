using System.Text.Json.Serialization;

namespace Unload.Catalog;

public record CatalogRoot(
    [property: JsonPropertyName("groups")] List<CatalogGroup> Groups,
    [property: JsonPropertyName("members")] List<CatalogMember> Members);

public record CatalogGroup(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("folder")] string Folder);

public record CatalogMember(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("groups")] List<int> Groups);
