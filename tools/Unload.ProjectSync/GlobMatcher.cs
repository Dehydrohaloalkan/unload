using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Unload.ProjectSync;

public sealed class GlobMatcher
{
    private readonly ConcurrentDictionary<string, Regex> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool IsMatch(string relativePath, IEnumerable<string> patterns)
    {
        var normalizedPath = Normalize(relativePath);
        return patterns.Any(pattern => GetRegex(pattern).IsMatch(normalizedPath));
    }

    public bool ShouldPruneDirectory(string relativeDirectory, IEnumerable<string> patterns)
    {
        var normalized = Normalize(relativeDirectory).TrimEnd('/');
        return IsMatch($"{normalized}/__project_sync_probe__", patterns);
    }

    public static string Normalize(string path) => path
        .Replace('\\', '/')
        .TrimStart('.', '/');

    private Regex GetRegex(string pattern) => _cache.GetOrAdd(pattern, static value =>
    {
        var normalized = Normalize(value);
        var expression = new StringBuilder("^");

        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (character == '*')
            {
                var isDoubleStar = index + 1 < normalized.Length && normalized[index + 1] == '*';
                if (!isDoubleStar)
                {
                    expression.Append("[^/]*");
                    continue;
                }

                index++;
                var followedBySlash = index + 1 < normalized.Length && normalized[index + 1] == '/';
                if (followedBySlash)
                {
                    index++;
                    expression.Append("(?:.*/)?");
                }
                else
                {
                    expression.Append(".*");
                }

                continue;
            }

            if (character == '?')
            {
                expression.Append("[^/]");
                continue;
            }

            expression.Append(Regex.Escape(character.ToString()));
        }

        expression.Append('$');
        return new Regex(
            expression.ToString(),
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    });
}
