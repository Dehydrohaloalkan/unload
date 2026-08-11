using System.Text.Json;

namespace Unload.ProjectSync;

public sealed class SyncConfiguration
{
    private static readonly string[] RequiredIgnorePatterns =
    [
        ".git/**",
        "**/.git/**",
        "**/bin/**",
        "**/obj/**",
        "**/node_modules/**",
        "**/.angular/**",
        "**/dist/**",
        ".tools/**",
        "graphify-out/**",
        "output/**",
        "**/output/**",
        "observability/**",
        "ftp-root/**",
        "_sync-backups/**"
    ];

    public IReadOnlyList<RenameRule> Renames { get; init; } = [];

    public IReadOnlyList<PathMappingRule> PathMappings { get; init; } = [];

    public IReadOnlyList<NamespaceMappingRule> NamespaceMappings { get; init; } = [];

    public IReadOnlyList<string> Ignore { get; init; } = [];

    public IReadOnlyList<string> Protected { get; init; } = [];

    public IReadOnlyList<string> TransformTextIn { get; init; } = [];

    public string BackupDirectoryName { get; init; } = "_sync-backups";

    public IReadOnlyList<string> AllIgnorePatterns => RequiredIgnorePatterns
        .Concat(Ignore)
        .Append($"{BackupDirectoryName}/**")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static SyncConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Файл конфигурации синхронизации не найден.", path);
        }

        var json = File.ReadAllText(path);
        var configuration = JsonSerializer.Deserialize<SyncConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidOperationException("Файл конфигурации пуст или имеет неверный формат.");

        configuration.Validate();
        return configuration;
    }

    private void Validate()
    {
        foreach (var mapping in PathMappings)
        {
            ValidateRelativeMappingPath(mapping.From, "pathMappings.from", allowEmpty: false);
            ValidateRelativeMappingPath(mapping.To, "pathMappings.to", allowEmpty: true);
        }

        foreach (var mapping in NamespaceMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Symbol) ||
                string.IsNullOrWhiteSpace(mapping.From) ||
                string.IsNullOrWhiteSpace(mapping.To))
            {
                throw new InvalidOperationException(
                    "Поля symbol, from и to в namespaceMappings не могут быть пустыми.");
            }
        }

        foreach (var rename in Renames)
        {
            if (string.IsNullOrEmpty(rename.From))
            {
                throw new InvalidOperationException("Поле 'from' в renames не может быть пустым.");
            }

            if (rename.To.Contains("..", StringComparison.Ordinal) ||
                rename.To.Contains('/') ||
                rename.To.Contains('\\'))
            {
                throw new InvalidOperationException(
                    $"Значение rename.to '{rename.To}' не должно содержать '..' или разделители пути.");
            }
        }

        if (string.IsNullOrWhiteSpace(BackupDirectoryName) ||
            BackupDirectoryName.Contains("..", StringComparison.Ordinal) ||
            BackupDirectoryName.Contains('/') ||
            BackupDirectoryName.Contains('\\'))
        {
            throw new InvalidOperationException(
                "backupDirectoryName должен быть простым именем каталога без '..' и разделителей пути.");
        }
    }

    private static void ValidateRelativeMappingPath(string value, string field, bool allowEmpty)
    {
        if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) ||
            Path.IsPathRooted(value) ||
            GlobMatcher.Normalize(value).Split('/').Any(static part => part == ".."))
        {
            throw new InvalidOperationException(
                $"Поле '{field}' должно быть безопасным относительным путём без '..'.");
        }
    }
}

public sealed record RenameRule(string From, string To);

public sealed record PathMappingRule(string From, string To);

public sealed record NamespaceMappingRule(string Symbol, string From, string To);
