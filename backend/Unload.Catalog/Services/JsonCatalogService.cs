using System.Text.Json;
using System.Text.RegularExpressions;
using Unload.Core;

namespace Unload.Catalog;

public sealed class JsonCatalogService : ICatalogService
{
    private static readonly Regex ProfileCodePattern = new("^[A-Z0-9_]{2,32}$", RegexOptions.Compiled);
    private readonly string _catalogPath;
    private readonly string _scriptsDirectory;

    public JsonCatalogService(string catalogPath, string scriptsDirectory)
    {
        _catalogPath = Path.GetFullPath(catalogPath);
        _scriptsDirectory = Path.GetFullPath(scriptsDirectory);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ScriptDefinition>>> ResolveAsync(
        IReadOnlyCollection<string> profileCodes,
        CancellationToken cancellationToken)
    {
        var catalog = await LoadCatalogAsync(cancellationToken);
        var profileSet = new HashSet<string>(catalog.Profiles.Select(static x => x.Code), StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, IReadOnlyList<ScriptDefinition>>(StringComparer.OrdinalIgnoreCase);

        foreach (var profileCode in profileCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ValidateProfileCode(profileCode);

            if (!profileSet.Contains(profileCode))
            {
                throw new InvalidOperationException($"Profile '{profileCode}' not found in catalog.");
            }

            resolved[profileCode] = await LoadScriptsForProfileAsync(profileCode, cancellationToken);
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
        string profileCode,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_scriptsDirectory))
        {
            throw new DirectoryNotFoundException($"Scripts directory was not found: {_scriptsDirectory}");
        }

        var files = Directory
            .EnumerateFiles(_scriptsDirectory, $"{profileCode}_*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(GetScriptOrder)
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var scripts = new List<ScriptDefinition>(files.Length);
        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(file);
            if (!fullPath.StartsWith(_scriptsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Detected script path outside allowed scripts directory.");
            }

            var sqlText = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var scriptCode = Path.GetFileNameWithoutExtension(fullPath);
            scripts.Add(new ScriptDefinition(profileCode, scriptCode, fullPath, sqlText));
        }

        return scripts;
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
}
