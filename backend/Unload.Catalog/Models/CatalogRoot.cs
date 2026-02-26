using System.Text.Json.Serialization;

namespace Unload.Catalog;

public record CatalogRoot(
    [property: JsonPropertyName("profiles")] List<CatalogProfile> Profiles);

public record CatalogProfile(
    [property: JsonPropertyName("code")] string Code);
