using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unload.Console.CatalogSelection;

/// <summary>
/// Загрузчик каталога для консольного сценария выбора профилей.
/// Используется console-приложением для построения интерактивного списка профилей.
/// </summary>
internal static class CatalogSelectionLoader
{
    /// <summary>
    /// Загружает каталог и преобразует его в группы профилей для консольного выбора.
    /// </summary>
    /// <param name="catalogPath">Путь к JSON-каталогу.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Список групп с профилями для UI-мультиселекта.</returns>
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

/// <summary>
/// Корневая JSON-модель каталога для консольного загрузчика.
/// </summary>
/// <param name="Groups">Список групп каталога.</param>
/// <param name="Members">Список участников каталога.</param>
internal record SelectionCatalogRoot(
    [property: JsonPropertyName("groups")] List<SelectionGroup> Groups,
    [property: JsonPropertyName("members")] List<SelectionMember> Members);

/// <summary>
/// JSON-модель группы для консольного загрузчика.
/// </summary>
/// <param name="Id">Идентификатор группы.</param>
/// <param name="Name">Название группы.</param>
/// <param name="Folder">Папка группы.</param>
internal record SelectionGroup(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("folder")] string Folder);

/// <summary>
/// JSON-модель участника для консольного загрузчика.
/// </summary>
/// <param name="Id">Идентификатор участника.</param>
/// <param name="Name">Название участника.</param>
/// <param name="Code">Код участника.</param>
/// <param name="Groups">Список идентификаторов групп участника.</param>
internal record SelectionMember(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("groups")] List<int> Groups);

/// <summary>
/// Source-generation контекст сериализации каталога для console.
/// Используется для типобезопасной и производительной десериализации JSON.
/// </summary>
[JsonSerializable(typeof(SelectionCatalogRoot))]
internal partial class CatalogSelectionJsonContext : JsonSerializerContext;
