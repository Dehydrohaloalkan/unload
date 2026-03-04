using System.Text.Json;
using Unload.Core;

namespace Unload.Catalog;

/// <summary>
/// Сервис чтения каталога из JSON и резолва target-кодов в SQL-скрипты.
/// Используется раннером и API для получения структуры каталога и набора скриптов к выполнению.
/// </summary>
public class JsonCatalogService : ICatalogService
{
    private readonly string _catalogPath;
    private readonly string _scriptsDirectory;

    /// <summary>
    /// Инициализирует сервис путями к файлу каталога и директории скриптов.
    /// </summary>
    /// <param name="catalogPath">Путь к <c>catalog.json</c>.</param>
    /// <param name="scriptsDirectory">Путь к корню SQL-скриптов.</param>
    public JsonCatalogService(string catalogPath, string scriptsDirectory)
    {
        _catalogPath = Path.GetFullPath(catalogPath);
        _scriptsDirectory = Path.GetFullPath(scriptsDirectory);
    }

    /// <summary>
    /// Загружает каталог и возвращает модель без дополнительной нормализации значений.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены чтения.</param>
    /// <returns>Каталог групп, участников и вычисленных target-выборок.</returns>
    public async Task<CatalogInfo> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var catalog = await LoadCatalogAsync(cancellationToken);
        var groupsById = catalog.Groups.ToDictionary(static x => x.Id);
        var memberGroupCodes = BuildMemberGroupCodes(catalog, groupsById);
        var targets = BuildTargets(catalog, groupsById);
        var groups = BuildGroups(catalog);
        var members = BuildMembers(catalog, memberGroupCodes);

        return new CatalogInfo(groups, members, targets);
    }

    /// <summary>
    /// Резолвит выбранные target-коды в отсортированные списки SQL-скриптов.
    /// </summary>
    /// <param name="targetCodes">Target-коды для резолва.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Словарь target-код -> список определений скриптов.</returns>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>>> ResolveAsync(
        IReadOnlyCollection<string> targetCodes,
        CancellationToken cancellationToken)
    {
        var catalogInfo = await GetCatalogAsync(cancellationToken);
        var targetMap = catalogInfo.Targets.ToDictionary(static x => x.TargetCode, StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, IReadOnlyList<ScriptDefinition>>(StringComparer.OrdinalIgnoreCase);

        foreach (var targetCode in targetCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!targetMap.TryGetValue(targetCode, out var target))
            {
                throw new InvalidOperationException($"Target '{targetCode}' not found in catalog.");
            }

            resolved[targetCode] = await LoadScriptsForTargetAsync(target, cancellationToken);
        }

        return resolved;
    }

    /// <summary>
    /// Загружает и десериализует исходный JSON-каталог.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены чтения.</param>
    /// <returns>Корневая модель десериализованного каталога.</returns>
    private async Task<CatalogRoot> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(_catalogPath);
        var result = await JsonSerializer.DeserializeAsync(
            stream,
            CatalogSerializerContext.Default.CatalogRoot,
            cancellationToken);

        return result ?? throw new InvalidOperationException("Catalog is empty or invalid.");
    }

    /// <summary>
    /// Загружает SQL-скрипты для конкретного target-кода с проверками безопасности путей.
    /// </summary>
    /// <param name="target">Target, для которого выбираются скрипты.</param>
    /// <param name="cancellationToken">Токен отмены загрузки.</param>
    /// <returns>Список определений скриптов target-кода.</returns>
    private async Task<IReadOnlyList<ScriptDefinition>> LoadScriptsForTargetAsync(
        CatalogTargetInfo target,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_scriptsDirectory))
        {
            throw new DirectoryNotFoundException($"Scripts directory was not found: {_scriptsDirectory}");
        }

        var groupDirectory = Path.GetFullPath(Path.Combine(_scriptsDirectory, target.GroupFolder));
        if (!groupDirectory.StartsWith(_scriptsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Detected scripts group path outside allowed scripts directory.");
        }

        if (!Directory.Exists(groupDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Scripts group directory was not found: {groupDirectory}");
        }

        var files = Directory
            .EnumerateFiles(groupDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .Where(path => CatalogScriptPathHelper.IsScriptForTarget(
                path,
                target.MemberCode,
                target.GroupCode,
                target.MemberFileExtension))
            .OrderBy(CatalogScriptPathHelper.GetScriptOrder)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var scripts = new List<ScriptDefinition>(files.Length);
        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(file);
            if (!fullPath.StartsWith(groupDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Detected script path outside allowed scripts directory.");
            }

            var sqlText = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var scriptCode = Path.GetFileNameWithoutExtension(fullPath);
            var scriptNameParts = CatalogScriptPathHelper.ParseScriptName(scriptCode);
            scripts.Add(new ScriptDefinition(
                target.TargetCode,
                scriptCode,
                scriptNameParts.Prefix,
                target.MemberFileExtension,
                scriptNameParts.ScriptType,
                scriptNameParts.ScriptCodes,
                scriptNameParts.FirstCodeDigit,
                target.MemberRawName,
                fullPath,
                sqlText));
        }

        return scripts;
    }

    private static string BuildGroupDisplayName(string groupName, string groupFolder)
    {
        return $"{groupName} ({groupFolder})";
    }

    /// <summary>
    /// Строит справочник кодов групп по каждому участнику каталога.
    /// </summary>
    private static Dictionary<int, string[]> BuildMemberGroupCodes(
        CatalogRoot catalog,
        IReadOnlyDictionary<int, CatalogGroup> groupsById)
    {
        var result = new Dictionary<int, string[]>();

        foreach (var member in catalog.Members)
        {
            var uniqueGroupCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var groupId in member.Groups.Distinct())
            {
                if (!groupsById.TryGetValue(groupId, out var group))
                {
                    throw new InvalidOperationException($"Group '{groupId}' was not found in catalog.");
                }

                uniqueGroupCodes.Add(group.Code);
            }

            result[member.Id] = uniqueGroupCodes
                .OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return result;
    }

    /// <summary>
    /// Вычисляет target-выборки для всех комбинаций участник-группа.
    /// </summary>
    private static CatalogTargetInfo[] BuildTargets(
        CatalogRoot catalog,
        IReadOnlyDictionary<int, CatalogGroup> groupsById)
    {
        var targetsByCode = new Dictionary<string, CatalogTargetInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in catalog.Members)
        {
            var memberCode = member.Code;
            var fileExtension = member.File;

            foreach (var groupId in member.Groups.Distinct())
            {
                if (!groupsById.TryGetValue(groupId, out var group))
                {
                    throw new InvalidOperationException($"Group '{groupId}' was not found in catalog.");
                }

                var groupFolder = group.Folder;
                var groupCode = group.Code;
                var targetCode = CatalogScriptPathHelper.BuildTargetCode(groupFolder, memberCode);

                targetsByCode[targetCode] = new CatalogTargetInfo(
                    targetCode,
                    group.Id,
                    member.Id,
                    BuildGroupDisplayName(group.Name, groupFolder),
                    groupFolder,
                    groupCode,
                    BuildMemberDisplayName(
                        member.Name,
                        memberCode,
                        fileExtension,
                        [groupCode]),
                    memberCode,
                    fileExtension,
                    member.Name);
            }
        }

        return targetsByCode.Values
            .OrderBy(static x => x.TargetCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Формирует список групп каталога.
    /// </summary>
    private static CatalogGroupInfo[] BuildGroups(CatalogRoot catalog)
    {
        var groups = new CatalogGroupInfo[catalog.Groups.Count];

        for (var i = 0; i < catalog.Groups.Count; i++)
        {
            var group = catalog.Groups[i];
            var folder = group.Folder;
            var code = group.Code;

            groups[i] = new CatalogGroupInfo(
                group.Id,
                BuildGroupDisplayName(group.Name, folder),
                folder,
                code);
        }

        return groups;
    }

    /// <summary>
    /// Формирует список участников каталога.
    /// </summary>
    private static CatalogMemberInfo[] BuildMembers(
        CatalogRoot catalog,
        IReadOnlyDictionary<int, string[]> memberGroupCodes)
    {
        var members = new CatalogMemberInfo[catalog.Members.Count];

        for (var i = 0; i < catalog.Members.Count; i++)
        {
            var member = catalog.Members[i];
            var code = member.Code;
            var fileExtension = member.File;
            var groupCodes = memberGroupCodes.TryGetValue(member.Id, out var existingCodes)
                ? existingCodes
                : [];

            members[i] = new CatalogMemberInfo(
                member.Id,
                BuildMemberDisplayName(member.Name, code, fileExtension, groupCodes),
                code,
                fileExtension);
        }

        return members;
    }

    private static string BuildMemberDisplayName(
        string memberName,
        string memberCode,
        string fileExtension,
        IReadOnlyList<string> groupCodes)
    {
        var extensionSuffix = fileExtension.StartsWith(".", StringComparison.Ordinal)
            ? fileExtension
            : $".{fileExtension}";
        var groupCodeMask = groupCodes.Count > 0 ? groupCodes[0] : "*";
        return $"{memberName} (Y{memberCode}{groupCodeMask}*{extensionSuffix})";
    }
}
