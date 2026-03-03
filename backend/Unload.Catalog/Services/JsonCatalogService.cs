using System.Text.Json;
using System.Text.RegularExpressions;
using Unload.Core;

namespace Unload.Catalog;

public class JsonCatalogService : ICatalogService
{
    private static readonly Regex ProfileCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);
    private static readonly Regex GroupFolderPattern = new("^[A-Z0-9_]{3,32}$", RegexOptions.Compiled);
    private static readonly Regex MemberCodePattern = new("^[A-Z0-9]{1,8}$", RegexOptions.Compiled);
    private static readonly Regex MemberFileExtensionPattern = new("^\\.[A-Z0-9]{1,8}$", RegexOptions.Compiled);
    private readonly string _catalogPath;
    private readonly string _scriptsDirectory;

    public JsonCatalogService(string catalogPath, string scriptsDirectory)
    {
        _catalogPath = Path.GetFullPath(catalogPath);
        _scriptsDirectory = Path.GetFullPath(scriptsDirectory);
    }

    public async Task<CatalogInfo> GetCatalogAsync(CancellationToken cancellationToken)
    {
        var catalog = await LoadCatalogAsync(cancellationToken);
        var groupsById = catalog.Groups.ToDictionary(static x => x.Id);

        var profiles = catalog.Members
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

                return new CatalogProfileInfo(
                    BuildProfileCode(normalizedGroupFolder, normalizedMemberCode),
                    group.Id,
                    member.Id,
                    group.Name,
                    normalizedGroupFolder,
                    member.Name,
                    normalizedMemberCode,
                    normalizedFileExtension);
            })
            .DistinctBy(static x => x.ProfileCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x.ProfileCode, StringComparer.OrdinalIgnoreCase)
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
            profiles);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>>> ResolveAsync(
        IReadOnlyCollection<string> profileCodes,
        CancellationToken cancellationToken)
    {
        var catalogInfo = await GetCatalogAsync(cancellationToken);
        var profileMap = catalogInfo.Profiles.ToDictionary(static x => x.ProfileCode, StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, IReadOnlyList<ScriptDefinition>>(StringComparer.OrdinalIgnoreCase);

        foreach (var profileCode in profileCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ValidateProfileCode(profileCode);

            if (!profileMap.TryGetValue(profileCode, out var profile))
            {
                throw new InvalidOperationException($"Profile '{profileCode}' not found in catalog.");
            }

            resolved[profileCode] = await LoadScriptsForProfileAsync(profile, cancellationToken);
        }

        return resolved;
    }

    private async Task<CatalogRoot> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(_catalogPath);
        var result = await JsonSerializer.DeserializeAsync(
            stream,
            CatalogSerializerContext.Default.CatalogRoot,
            cancellationToken);

        return result ?? throw new InvalidOperationException("Catalog is empty or invalid.");
    }

    private async Task<IReadOnlyList<ScriptDefinition>> LoadScriptsForProfileAsync(
        CatalogProfileInfo profile,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_scriptsDirectory))
        {
            throw new DirectoryNotFoundException($"Scripts directory was not found: {_scriptsDirectory}");
        }

        var groupDirectory = Path.GetFullPath(Path.Combine(_scriptsDirectory, profile.GroupFolder));
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
            .Where(path => IsScriptForMember(path, profile.MemberCode))
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
            var outputFileStem = BuildOutputFileStem(profile.MemberCode, profile.GroupFolder, scriptCode);
            scripts.Add(new ScriptDefinition(
                profile.ProfileCode,
                scriptCode,
                outputFileStem,
                profile.MemberFileExtension,
                fullPath,
                sqlText));
        }

        return scripts;
    }

    private static bool IsScriptForMember(string scriptPath, string memberCode)
    {
        var fileName = Path.GetFileNameWithoutExtension(scriptPath);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length < 2)
        {
            return false;
        }

        return char.ToUpperInvariant(fileName[1]) == char.ToUpperInvariant(memberCode[0]);
    }

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

    private static void ValidateProfileCode(string profileCode)
    {
        if (!ProfileCodePattern.IsMatch(profileCode))
        {
            throw new InvalidOperationException($"Profile code '{profileCode}' is invalid.");
        }
    }

    private static void ValidateGroupFolder(string folder)
    {
        var normalized = folder.Trim().ToUpperInvariant();
        if (!GroupFolderPattern.IsMatch(normalized))
        {
            throw new InvalidOperationException($"Group folder '{folder}' is invalid.");
        }
    }

    private static void ValidateMemberCode(string memberCode)
    {
        var normalized = memberCode.Trim().ToUpperInvariant();
        if (!MemberCodePattern.IsMatch(normalized))
        {
            throw new InvalidOperationException($"Member code '{memberCode}' is invalid.");
        }
    }

    private static void ValidateMemberFileExtension(string memberFileExtension)
    {
        var normalized = memberFileExtension.Trim().ToUpperInvariant();
        if (!MemberFileExtensionPattern.IsMatch(normalized))
        {
            throw new InvalidOperationException(
                $"Member file extension '{memberFileExtension}' is invalid.");
        }
    }

    private static string BuildProfileCode(string groupFolder, string memberCode)
    {
        return $"{groupFolder}_{memberCode}";
    }

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
