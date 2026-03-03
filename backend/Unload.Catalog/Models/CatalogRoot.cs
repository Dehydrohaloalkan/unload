using System.Text.Json.Serialization;

namespace Unload.Catalog;

/// <summary>
/// Корневая JSON-модель каталога из <c>configs/catalog.json</c>.
/// Используется только на этапе десериализации каталога.
/// </summary>
/// <param name="Groups">Список групп каталога.</param>
/// <param name="Members">Список участников каталога.</param>
public record CatalogRoot(
    [property: JsonPropertyName("groups")] List<CatalogGroup> Groups,
    [property: JsonPropertyName("members")] List<CatalogMember> Members);

/// <summary>
/// JSON-модель группы каталога.
/// Используется для построения target-выборок и поиска SQL-папок.
/// </summary>
/// <param name="Id">Идентификатор группы.</param>
/// <param name="Name">Имя группы.</param>
/// <param name="Folder">Имя папки группы в директории скриптов.</param>
public record CatalogGroup(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("folder")] string Folder);

/// <summary>
/// JSON-модель участника каталога.
/// Используется для построения target-кода и расширения выходного файла.
/// </summary>
/// <param name="Id">Идентификатор участника.</param>
/// <param name="Name">Имя участника.</param>
/// <param name="Code">Код участника.</param>
/// <param name="File">Расширение файла результата участника.</param>
/// <param name="Groups">Список идентификаторов групп, к которым относится участник.</param>
public record CatalogMember(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("groups")] List<int> Groups);
