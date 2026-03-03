using System.Text.Json;

namespace Unload.Console.CatalogSelection;

/// <summary>
/// Загрузчик каталога для консольного сценария выбора target-выборок.
/// Используется console-приложением для построения интерактивного списка target-кодов.
/// </summary>
internal static class CatalogSelectionLoader
{
    /// <summary>
    /// Загружает каталог и преобразует его в группы target-выборок для консольного выбора.
    /// </summary>
    /// <param name="catalogPath">Путь к JSON-каталогу.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Список групп с target-выборками для UI-мультиселекта.</returns>
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

        var groupedTargets = root.Groups
            .Select(groupData =>
            {
                var targets = membersById.Values
                    .Where(member => member.Groups.Contains(groupData.Id))
                    .Select(member =>
                    {
                        var normalizedFolder = groupData.Folder.Trim().ToUpperInvariant();
                        var normalizedCode = member.Code.Trim().ToUpperInvariant();
                        var targetCode = $"{normalizedFolder}_{normalizedCode}";

                        return new CatalogSelectionTarget(targetCode, member.Name, normalizedCode);
                    })
                    .DistinctBy(static x => x.TargetCode, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static x => x.MemberName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new CatalogSelectionGroup(groupData.Id, groupData.Name, targets);
            })
            .OrderBy(static x => x.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return groupedTargets;
    }
}
