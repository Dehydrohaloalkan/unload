using System.Text.Json.Serialization;

namespace Unload.Catalog;

public sealed record CatalogRoot(
    [property: JsonPropertyName("profiles")] List<CatalogProfile> Profiles);

public sealed record CatalogProfile(
    [property: JsonPropertyName("code")] string Code);
