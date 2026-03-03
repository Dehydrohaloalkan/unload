using System.Text.Json;
using System.Text.RegularExpressions;
using Unload.Core;

namespace Unload.Catalog;

/// <summary>
/// Сервис чтения каталога из JSON и резолва target-кодов в SQL-скрипты.
/// Используется раннером и API для получения структуры каталога и набора скриптов к выполнению.
/// </summary>
public class JsonCatalogService : ICatalogService
{
    private static readonly Regex TargetCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);
    private static readonly Regex GroupFolderPattern = new("^[A-Z0-9_]{3,32}$", RegexOptions.Compiled);
    private static readonly Regex MemberCodePattern = new("^[A-Z0-9]{1,8}$", RegexOptions.Compiled);
    private static readonly Regex MemberFileExtensionPattern = new("^\\.[A-Z0-9]{1,8}$", RegexOptions.Compiled);
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

        var targets = catalog.Members
            .SelectMany(member => member.Groups.Distinct().Select(groupId => (Member: member, GroupId: groupId)))
            .Select(entry =>
            {
                if (!groupsById.TryGetValue(entry.GroupId, out var group))
                {
                    throw new InvalidOperationException($"Group '{entry.GroupId}' was not found in catalog.");
                }

                var member = entry.Member;
                ValidateGroupFolder(group.Folder);
                ValidateMemberCode(member.Code);
                ValidateMemberFileExtension(member.File);

                var normalizedGroupFolder = group.Folder.Trim().ToUpperInvariant();
                var normalizedMemberCode = member.Code.Trim().ToUpperInvariant();
                var normalizedFileExtension = member.File.Trim().ToUpperInvariant();

                return new CatalogTargetInfo(
                    BuildTargetCode(normalizedGroupFolder, normalizedMemberCode),
                    group.Id,
                    member.Id,
                    group.Name,
                    normalizedGroupFolder,
                    member.Name,
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
                    ValidateGroupFolder(group.Folder);
                    return new CatalogGroupInfo(group.Id, group.Name, group.Folder.ToUpperInvariant());
                })
                .ToArray(),
            catalog.Members
                .Select(member =>
                {
                    ValidateMemberCode(member.Code);
                    ValidateMemberFileExtension(member.File);
                    return new CatalogMemberInfo(
                        member.Id,
                        member.Name,
                        member.Code.Trim().ToUpperInvariant(),
                        member.File.Trim().ToUpperInvariant());
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
            ValidateTargetCode(targetCode);

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
            .Where(path => IsScriptForMember(path, target.MemberCode))
            .OrderBy(GetScriptOrder)
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
            var outputFileStem = BuildOutputFileStem(target.MemberCode, target.GroupFolder, scriptCode);
            scripts.Add(new ScriptDefinition(
                target.TargetCode,
                scriptCode,
                outputFileStem,
                target.MemberFileExtension,
                fullPath,
                sqlText));
        }

        return scripts;
    }

    /// <summary>
    /// Проверяет, относится ли SQL-файл к участнику по второй букве имени файла.
    /// </summary>
    /// <param name="scriptPath">Путь к SQL-файлу.</param>
    /// <param name="memberCode">Код участника target-выборки.</param>
    /// <returns><c>true</c>, если файл относится к участнику; иначе <c>false</c>.</returns>
    private static bool IsScriptForMember(string scriptPath, string memberCode)
    {
        var fileName = Path.GetFileNameWithoutExtension(scriptPath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length < 2)
        {
            return false;
        }

        return char.ToUpperInvariant(fileName[1]) == char.ToUpperInvariant(memberCode[0]);
    }

    /// <summary>
    /// Вычисляет порядок скрипта по числовому суффиксу после последнего <c>_</c>.
    /// </summary>
    /// <param name="filePath">Путь к SQL-файлу.</param>
    /// <returns>Числовой порядок или <see cref="int.MaxValue"/>, если суффикс не найден.</returns>
    private static int GetScriptOrder(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var lastUnderscore = fileName.LastIndexOf('_');
        if (lastUnderscore < 0)
        {
            return int.MaxValue;
        }

        var suffix = fileName[(lastUnderscore + 1)..];
        return int.TryParse(suffix, out var number) ? number : int.MaxValue;
    }

    /// <summary>
    /// Валидирует синтаксис target-кода.
    /// </summary>
    /// <param name="targetCode">Target-код.</param>
    private static void ValidateTargetCode(string targetCode)
    {
        if (!TargetCodePattern.IsMatch(targetCode))
        {
            throw new InvalidOperationException($"Target code '{targetCode}' is invalid.");
        }
    }

    /// <summary>
    /// Валидирует имя папки группы каталога.
    /// </summary>
    /// <param name="folder">Имя папки группы.</param>
    private static void ValidateGroupFolder(string folder)
    {
        var normalized = folder.Trim().ToUpperInvariant();
        if (!GroupFolderPattern.IsMatch(normalized))
        {
            throw new InvalidOperationException($"Group folder '{folder}' is invalid.");
        }
    }

    /// <summary>
    /// Валидирует код участника каталога.
    /// </summary>
    /// <param name="memberCode">Код участника.</param>
    private static void ValidateMemberCode(string memberCode)
    {
        var normalized = memberCode.Trim().ToUpperInvariant();
        if (!MemberCodePattern.IsMatch(normalized))
        {
            throw new InvalidOperationException($"Member code '{memberCode}' is invalid.");
        }
    }

    /// <summary>
    /// Валидирует расширение выходного файла участника.
    /// </summary>
    /// <param name="memberFileExtension">Расширение файла, заданное в каталоге.</param>
    private static void ValidateMemberFileExtension(string memberFileExtension)
    {
        var normalized = memberFileExtension.Trim().ToUpperInvariant();
        if (!MemberFileExtensionPattern.IsMatch(normalized))
        {
            throw new InvalidOperationException(
                $"Member file extension '{memberFileExtension}' is invalid.");
        }
    }

    /// <summary>
    /// Строит target-код в формате <c>GROUP_MEMBER</c>.
    /// </summary>
    /// <param name="groupFolder">Папка группы.</param>
    /// <param name="memberCode">Код участника.</param>
    /// <returns>Нормализованный target-код.</returns>
    private static string BuildTargetCode(string groupFolder, string memberCode)
    {
        return $"{groupFolder}_{memberCode}";
    }

    /// <summary>
    /// Строит базовую часть имени выходного файла по правилам формата выгрузки.
    /// </summary>
    /// <param name="memberCode">Код участника target-выборки.</param>
    /// <param name="groupFolder">Папка группы target-выборки.</param>
    /// <param name="scriptCode">Код скрипта (имя SQL-файла без расширения).</param>
    /// <returns>Базовая часть имени выходного файла без суффикса чанка и расширения.</returns>
    private static string BuildOutputFileStem(string memberCode, string groupFolder, string scriptCode)
    {
        if (scriptCode.Length < 3)
        {
            throw new InvalidOperationException(
                $"Script '{scriptCode}' must have at least 3 characters in file name.");
        }

        var tail = scriptCode[3..].TrimStart('_');
        var groupThirdLetter = groupFolder[2];
        return string.IsNullOrWhiteSpace(tail)
            ? $"Y{memberCode}{groupThirdLetter}"
            : $"Y{memberCode}{groupThirdLetter}_{tail}";
    }
}
