namespace Unload.ProjectSync;

public sealed class GitSyncPlanner(
    GitClient gitClient,
    GlobMatcher globMatcher,
    TextFileTransformer textTransformer)
{
    private readonly GitClient _gitClient = gitClient;
    private readonly GlobMatcher _globMatcher = globMatcher;
    private readonly TextFileTransformer _textTransformer = textTransformer;

    public SyncPlan CreatePlan(
        string repositoryRoot,
        string targetRoot,
        string commit,
        SyncConfiguration configuration)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetRoot));
        if (!Directory.Exists(source) || !Directory.Exists(target))
        {
            throw new DirectoryNotFoundException("Исходный Git-репозиторий или целевой каталог не найден.");
        }

        EnsureRootsAreIndependent(source, target);
        var pathMapper = new SyncPlanner(_globMatcher, _textTransformer);
        var desiredChanges = new Dictionary<string, DesiredGitChange>(StringComparer.OrdinalIgnoreCase);

        var resolvedCommit = _gitClient.ResolveCommit(source, commit);
        foreach (var change in _gitClient.GetChangesForCommit(source, resolvedCommit))
        {
            switch (change.Kind)
            {
                case GitChangeKind.Add:
                case GitChangeKind.Modify:
                    AddWrite(change.NewPath!);
                    break;
                case GitChangeKind.Delete:
                    AddDelete(change.OldPath!);
                    break;
                case GitChangeKind.Rename:
                {
                    var oldTarget = pathMapper.ApplyRenames(change.OldPath!, configuration.Renames);
                    var newTarget = pathMapper.ApplyRenames(change.NewPath!, configuration.Renames);
                    if (!string.Equals(oldTarget, newTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        AddDelete(change.OldPath!);
                    }

                    AddWrite(change.NewPath!);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(change.Kind), change.Kind, null);
            }
        }

        var items = new List<SyncPlanItem>();
        var sameCount = 0;
        var ignoredCount = 0;

        foreach (var desired in desiredChanges.Values)
        {
            var targetPath = ResolveSafeTargetPath(target, desired.TargetRelativePath);
            if (IsIgnored(desired.SourceRelativePath, desired.TargetRelativePath, configuration))
            {
                ignoredCount++;
                continue;
            }

            var isProtected = IsProtected(desired.SourceRelativePath, desired.TargetRelativePath, configuration);
            if (desired.Delete)
            {
                if (!File.Exists(targetPath))
                {
                    sameCount++;
                    continue;
                }

                items.Add(new SyncPlanItem(
                    isProtected ? SyncAction.Protected : SyncAction.Delete,
                    desired.SourceRelativePath,
                    desired.TargetRelativePath,
                    SourceFullPath: null,
                    targetPath,
                    TransformText: false));
                continue;
            }

            var transformText = _globMatcher.IsMatch(desired.SourceRelativePath, configuration.TransformTextIn) ||
                                _globMatcher.IsMatch(desired.TargetRelativePath, configuration.TransformTextIn);
            var bytes = _gitClient.ReadFileAtCommit(source, resolvedCommit, desired.SourceRelativePath);
            var expectedBytes = transformText
                ? _textTransformer.Transform(bytes, configuration.Renames)
                : bytes;
            if (File.Exists(targetPath) &&
                new FileInfo(targetPath).Length == expectedBytes.Length &&
                File.ReadAllBytes(targetPath).AsSpan().SequenceEqual(expectedBytes))
            {
                sameCount++;
                continue;
            }

            items.Add(new SyncPlanItem(
                isProtected
                    ? SyncAction.Protected
                    : File.Exists(targetPath)
                        ? SyncAction.Update
                        : SyncAction.Add,
                desired.SourceRelativePath,
                desired.TargetRelativePath,
                SourceFullPath: null,
                targetPath,
                transformText,
                expectedBytes));
        }

        return new SyncPlan(
            source,
            target,
            items
                .OrderBy(static item => item.Action)
                .ThenBy(static item => item.TargetRelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            sameCount,
            ignoredCount);

        void AddWrite(string sourceRelativePath)
        {
            var normalizedSource = GlobMatcher.Normalize(sourceRelativePath);
            var mappedTarget = pathMapper.ApplyRenames(normalizedSource, configuration.Renames);
            AddDesired(new DesiredGitChange(normalizedSource, mappedTarget, Delete: false));
        }

        void AddDelete(string sourceRelativePath)
        {
            var normalizedSource = GlobMatcher.Normalize(sourceRelativePath);
            var mappedTarget = pathMapper.ApplyRenames(normalizedSource, configuration.Renames);
            AddDesired(new DesiredGitChange(normalizedSource, mappedTarget, Delete: true));
        }

        void AddDesired(DesiredGitChange desired)
        {
            if (desiredChanges.TryGetValue(desired.TargetRelativePath, out var existing) &&
                !string.Equals(existing.SourceRelativePath, desired.SourceRelativePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Два Git-пути после переименования указывают на один production-файл: " +
                    $"'{existing.SourceRelativePath}' и '{desired.SourceRelativePath}' -> '{desired.TargetRelativePath}'.");
            }

            desiredChanges[desired.TargetRelativePath] = desired;
        }
    }

    private bool IsIgnored(string sourceRelative, string targetRelative, SyncConfiguration configuration) =>
        _globMatcher.IsMatch(sourceRelative, configuration.AllIgnorePatterns) ||
        _globMatcher.IsMatch(targetRelative, configuration.AllIgnorePatterns);

    private bool IsProtected(string sourceRelative, string targetRelative, SyncConfiguration configuration) =>
        _globMatcher.IsMatch(sourceRelative, configuration.Protected) ||
        _globMatcher.IsMatch(targetRelative, configuration.Protected);

    private static string ResolveSafeTargetPath(string targetRoot, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(targetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = targetRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidOperationException($"Преобразованный путь вышел за пределы production: {relativePath}");
        }

        return fullPath;
    }

    private static void EnsureRootsAreIndependent(string source, string target)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var sourceWithSeparator = source + Path.DirectorySeparatorChar;
        var targetWithSeparator = target + Path.DirectorySeparatorChar;
        if (string.Equals(source, target, comparison) ||
            sourceWithSeparator.StartsWith(targetWithSeparator, comparison) ||
            targetWithSeparator.StartsWith(sourceWithSeparator, comparison))
        {
            throw new InvalidOperationException(
                "Git-репозиторий и production-каталог должны быть разными и не могут быть вложены друг в друга.");
        }
    }

    private sealed record DesiredGitChange(
        string SourceRelativePath,
        string TargetRelativePath,
        bool Delete);
}
