using System.Text.Json.Serialization;

namespace Unload.Catalog;

/// <summary>
/// Корневая JSON-модель каталога из <c>configs/catalog.json</c>.
/// </summary>
/// <param name="Groups">Список групп каталога.</param>
/// <param name="Members">Список участников каталога.</param>
/// <param name="BigScripts">Опционально: target-выборки (member+group), скрипты которых считаются «большими» и выполняются в n-1 потоках.</param>
public record CatalogRoot(
    [property: JsonPropertyName("groups")] List<CatalogGroup> Groups,
    [property: JsonPropertyName("members")] List<CatalogMember> Members,
    [property: JsonPropertyName("bigScripts")] List<CatalogBigScript>? BigScripts = null);

/// <summary>
/// Ссылка на target-выборку в каталоге (member+group), чьи скрипты считаются «большими».
/// </summary>
public record CatalogBigScript(
    [property: JsonPropertyName("memberId")] int MemberId,
    [property: JsonPropertyName("groupId")] int GroupId);

/// <summary>
/// JSON-модель группы каталога.
/// Используется для построения target-выборок и поиска SQL-папок.
/// </summary>
/// <param name="Id">Идентификатор группы.</param>
/// <param name="Name">Имя группы.</param>
/// <param name="Folder">Имя папки группы в директории скриптов.</param>
/// <param name="Code">Код группы для имен SQL-скриптов.</param>
public record CatalogGroup(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("folder")] string Folder,
    [property: JsonPropertyName("code")] string Code);

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
