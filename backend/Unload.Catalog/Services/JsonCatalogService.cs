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
    /// Загружает каталог, валидирует его и возвращает нормализованную модель.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены чтения.</param>
    /// <returns>Каталог групп, участников и вычисленных target-выборок.</returns>
    public async Task<CatalogInfo> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var catalog = await LoadCatalogAsync(cancellationToken);
        var groupsById = catalog.Groups.ToDictionary(static x => x.Id);
        var memberGroupCodes = catalog.Members.ToDictionary(
            static member => member.Id,
            member => member.Groups
                .Distinct()
                .Select(groupId =>
                {
                    if (!groupsById.TryGetValue(groupId, out var group))
                    {
                        throw new InvalidOperationException($"Group '{groupId}' was not found in catalog.");
                    }

                    CatalogValidation.ValidateGroupCode(group.Code);
                    return group.Code.Trim().ToUpperInvariant();
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        var targets = catalog.Members
            .SelectMany(member => member.Groups.Distinct().Select(groupId => (Member: member, GroupId: groupId)))
            .Select(entry =>
            {
                if (!groupsById.TryGetValue(entry.GroupId, out var group))
                {
                    throw new InvalidOperationException($"Group '{entry.GroupId}' was not found in catalog.");
                }

                var member = entry.Member;
                CatalogValidation.ValidateGroupFolder(group.Folder);
                CatalogValidation.ValidateGroupCode(group.Code);
                CatalogValidation.ValidateMemberCode(member.Code);
                CatalogValidation.ValidateMemberFileExtension(member.File);

                var normalizedGroupFolder = group.Folder.Trim().ToUpperInvariant();
                var normalizedGroupCode = group.Code.Trim().ToUpperInvariant();
                var normalizedMemberCode = member.Code.Trim().ToUpperInvariant();
                var normalizedFileExtension = member.File.Trim().ToUpperInvariant();

                return new CatalogTargetInfo(
                    CatalogScriptPathHelper.BuildTargetCode(normalizedGroupFolder, normalizedMemberCode),
                    group.Id,
                    member.Id,
                    BuildGroupDisplayName(group.Name, normalizedGroupFolder),
                    normalizedGroupFolder,
                    normalizedGroupCode,
                    BuildMemberDisplayName(
                        member.Name,
                        normalizedMemberCode,
                        normalizedFileExtension,
                        [normalizedGroupCode]),
                    normalizedMemberCode,
                    normalizedFileExtension);
            })
            .DistinctBy(static x => x.TargetCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x.TargetCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CatalogInfo(
            catalog.Groups
                .Select(group =>
                {
                    CatalogValidation.ValidateGroupFolder(group.Folder);
                    CatalogValidation.ValidateGroupCode(group.Code);
                    var normalizedFolder = group.Folder.Trim().ToUpperInvariant();
                    var normalizedCode = group.Code.Trim().ToUpperInvariant();
                    return new CatalogGroupInfo(
                        group.Id,
                        BuildGroupDisplayName(group.Name, normalizedFolder),
                        normalizedFolder,
                        normalizedCode);
                })
                .ToArray(),
            catalog.Members
                .Select(member =>
                {
                    CatalogValidation.ValidateMemberCode(member.Code);
                    CatalogValidation.ValidateMemberFileExtension(member.File);
                    var normalizedCode = member.Code.Trim().ToUpperInvariant();
                    var normalizedFileExtension = member.File.Trim().ToUpperInvariant();
                    var groupCodes = memberGroupCodes.TryGetValue(member.Id, out var existingCodes)
                        ? existingCodes
                        : [];
                    return new CatalogMemberInfo(
                        member.Id,
                        BuildMemberDisplayName(member.Name, normalizedCode, normalizedFileExtension, groupCodes),
                        normalizedCode,
                        normalizedFileExtension);
                })
                .ToArray(),
            targets);
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
            CatalogValidation.ValidateTargetCode(targetCode);

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
                fullPath,
                sqlText));
        }

        return scripts;
    }

    private static string BuildGroupDisplayName(string groupName, string groupFolder)
    {
        return $"{groupName} ({groupFolder})";
    }

    private static string BuildMemberDisplayName(
        string memberName,
        string memberCode,
        string fileExtension,
        IReadOnlyList<string> groupCodes)
    {
        var extensionWithoutDot = fileExtension.TrimStart('.');
        var groupCodeMask = groupCodes.Count > 0 ? groupCodes[0] : "*";
        return $"{memberName} (Y{memberCode}{groupCodeMask}*.{extensionWithoutDot})";
    }
}
