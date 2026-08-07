namespace Unload.ProjectSync;

public sealed class SyncPlanner(
    GlobMatcher globMatcher,
    TextFileTransformer textTransformer)
{
    private readonly GlobMatcher _globMatcher = globMatcher;
    private readonly TextFileTransformer _textTransformer = textTransformer;

    public SyncPlan CreatePlan(string sourceRoot, string targetRoot, SyncConfiguration configuration)
    {
        var source = NormalizeRoot(sourceRoot, mustExist: true);
        var target = NormalizeRoot(targetRoot, mustExist: true);
        EnsureRootsAreIndependent(source, target);

        var items = new List<SyncPlanItem>();
        var mappedTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sameCount = 0;
        var ignoredCount = 0;

        foreach (var sourceFile in EnumerateFiles(source, configuration.AllIgnorePatterns))
        {
            var sourceRelative = NormalizeRelative(Path.GetRelativePath(source, sourceFile));
            var targetRelative = ApplyRenames(sourceRelative, configuration.Renames);
            var targetFile = ResolveSafeTargetPath(target, targetRelative);
            mappedTargetPaths.Add(targetRelative);

            if (IsIgnored(sourceRelative, targetRelative, configuration))
            {
                ignoredCount++;
                continue;
            }

            var transformText = _globMatcher.IsMatch(sourceRelative, configuration.TransformTextIn) ||
                                _globMatcher.IsMatch(targetRelative, configuration.TransformTextIn);
            var filesAreEqual = File.Exists(targetFile) && FilesAreEqual(sourceFile, targetFile, transformText, configuration);
            if (filesAreEqual)
            {
                sameCount++;
                continue;
            }

            var action = IsProtected(sourceRelative, targetRelative, configuration)
                ? SyncAction.Protected
                : File.Exists(targetFile)
                    ? SyncAction.Update
                    : SyncAction.Add;

            items.Add(new SyncPlanItem(
                action,
                sourceRelative,
                targetRelative,
                sourceFile,
                targetFile,
                transformText));
        }

        foreach (var targetFile in EnumerateFiles(target, configuration.AllIgnorePatterns))
        {
            var targetRelative = NormalizeRelative(Path.GetRelativePath(target, targetFile));
            if (mappedTargetPaths.Contains(targetRelative) ||
                _globMatcher.IsMatch(targetRelative, configuration.AllIgnorePatterns))
            {
                continue;
            }

            items.Add(new SyncPlanItem(
                SyncAction.TargetOnly,
                SourceRelativePath: string.Empty,
                targetRelative,
                SourceFullPath: null,
                targetFile,
                TransformText: false));
        }

        var orderedItems = items
            .OrderBy(static item => item.Action)
            .ThenBy(static item => item.TargetRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SyncPlan(source, target, orderedItems, sameCount, ignoredCount);
    }

    public string ApplyRenames(string value, IReadOnlyList<RenameRule> renames)
    {
        var result = value;
        foreach (var rename in renames)
        {
            result = result.Replace(rename.From, rename.To, StringComparison.Ordinal);
        }

        return NormalizeRelative(result);
    }

    private bool FilesAreEqual(
        string sourceFile,
        string targetFile,
        bool transformText,
        SyncConfiguration configuration)
    {
        if (transformText)
        {
            var expectedBytes = _textTransformer.ReadAndTransform(sourceFile, configuration.Renames);
            var targetInfo = new FileInfo(targetFile);
            return targetInfo.Length == expectedBytes.Length &&
                   File.ReadAllBytes(targetFile).AsSpan().SequenceEqual(expectedBytes);
        }

        var sourceInfo = new FileInfo(sourceFile);
        var targetInfoRaw = new FileInfo(targetFile);
        if (sourceInfo.Length != targetInfoRaw.Length)
        {
            return false;
        }

        using var sourceStream = File.OpenRead(sourceFile);
        using var targetStream = File.OpenRead(targetFile);
        Span<byte> sourceBuffer = stackalloc byte[8192];
        Span<byte> targetBuffer = stackalloc byte[8192];

        while (true)
        {
            var sourceRead = sourceStream.Read(sourceBuffer);
            var targetRead = targetStream.Read(targetBuffer);
            if (sourceRead != targetRead)
            {
                return false;
            }

            if (sourceRead == 0)
            {
                return true;
            }

            if (!sourceBuffer[..sourceRead].SequenceEqual(targetBuffer[..targetRead]))
            {
                return false;
            }
        }
    }

    private bool IsIgnored(string sourceRelative, string targetRelative, SyncConfiguration configuration) =>
        _globMatcher.IsMatch(sourceRelative, configuration.AllIgnorePatterns) ||
        _globMatcher.IsMatch(targetRelative, configuration.AllIgnorePatterns);

    private bool IsProtected(string sourceRelative, string targetRelative, SyncConfiguration configuration) =>
        _globMatcher.IsMatch(sourceRelative, configuration.Protected) ||
        _globMatcher.IsMatch(targetRelative, configuration.Protected);

    private IEnumerable<string> EnumerateFiles(string root, IEnumerable<string> ignorePatterns)
    {
        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                yield return file;
            }

            foreach (var childDirectory in Directory.EnumerateDirectories(directory))
            {
                var relative = NormalizeRelative(Path.GetRelativePath(root, childDirectory));
                if (!_globMatcher.ShouldPruneDirectory(relative, ignorePatterns))
                {
                    directories.Push(childDirectory);
                }
            }
        }
    }

    private static string NormalizeRoot(string root, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Путь к каталогу не может быть пустым.");
        }

        var fullPath = Path.GetFullPath(root.Trim().Trim('"'));
        if (mustExist && !Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Каталог не найден: {fullPath}");
        }

        return Path.TrimEndingDirectorySeparator(fullPath);
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
                "Исходный и целевой каталоги должны быть разными и не могут находиться один внутри другого.");
        }
    }

    private static string ResolveSafeTargetPath(string targetRoot, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(targetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = targetRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(rootWithSeparator, comparison))
        {
            throw new InvalidOperationException($"Преобразованный путь вышел за пределы целевого каталога: {relativePath}");
        }

        return fullPath;
    }

    private static string NormalizeRelative(string path) => GlobMatcher.Normalize(path);
}
