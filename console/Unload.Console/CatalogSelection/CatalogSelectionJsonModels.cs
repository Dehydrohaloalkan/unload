using System.Text.Json.Serialization;

namespace Unload.Console.CatalogSelection;

/// <summary>
/// Модели JSON-каталога для консольного выбора target-кодов.
/// </summary>
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
