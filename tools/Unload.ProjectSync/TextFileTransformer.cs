using System.Text;
using System.Text.RegularExpressions;

namespace Unload.ProjectSync;

public sealed class TextFileTransformer
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public byte[] ReadAndTransform(string path, IReadOnlyList<RenameRule> renames)
    {
        return Transform(File.ReadAllBytes(path), renames);
    }

    public byte[] ReadAndTransform(
        string path,
        SyncConfiguration configuration,
        string sourceRelativePath,
        string targetRelativePath)
    {
        return Transform(
            File.ReadAllBytes(path),
            configuration,
            sourceRelativePath,
            targetRelativePath);
    }

    public byte[] Transform(byte[] bytes, IReadOnlyList<RenameRule> renames)
    {
        var format = DetectFormat(bytes);
        if (format is null)
        {
            return bytes;
        }

        var text = format.Encoding.GetString(bytes, format.PreambleLength, bytes.Length - format.PreambleLength);
        foreach (var rename in renames)
        {
            text = text.Replace(rename.From, rename.To, StringComparison.Ordinal);
        }

        var content = format.Encoding.GetBytes(text);
        if (format.PreambleLength == 0)
        {
            return content;
        }

        var preamble = format.Encoding.GetPreamble();
        var result = new byte[preamble.Length + content.Length];
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }

    public byte[] Transform(
        byte[] bytes,
        SyncConfiguration configuration,
        string sourceRelativePath,
        string targetRelativePath)
    {
        var format = DetectFormat(bytes);
        if (format is null)
        {
            return bytes;
        }

        var text = format.Encoding.GetString(bytes, format.PreambleLength, bytes.Length - format.PreambleLength);
        foreach (var rename in configuration.Renames)
        {
            text = text.Replace(rename.From, rename.To, StringComparison.Ordinal);
        }

        if (sourceRelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            targetRelativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            text = ApplyNamespaceMappings(text, configuration);
        }

        var content = format.Encoding.GetBytes(text);
        if (format.PreambleLength == 0)
        {
            return content;
        }

        var preamble = format.Encoding.GetPreamble();
        var result = new byte[preamble.Length + content.Length];
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }

    private static string ApplyNamespaceMappings(string text, SyncConfiguration configuration)
    {
        foreach (var mapping in configuration.NamespaceMappings)
        {
            var sourceNamespace = ApplyRenames(mapping.From, configuration.Renames);
            var symbolPattern = $@"\b{Regex.Escape(mapping.Symbol)}\b";
            if (!Regex.IsMatch(text, symbolPattern, RegexOptions.CultureInvariant))
            {
                continue;
            }

            var declarationPattern =
                $@"\b(?:class|record|struct|interface|enum)\s+{Regex.Escape(mapping.Symbol)}\b";
            if (Regex.IsMatch(text, declarationPattern, RegexOptions.CultureInvariant))
            {
                var namespacePattern =
                    $@"(?m)^(\s*namespace\s+){Regex.Escape(sourceNamespace)}(\s*[;{{])";
                text = Regex.Replace(
                    text,
                    namespacePattern,
                    $"$1{mapping.To}$2",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
                continue;
            }

            if (HasNamespace(text, mapping.To) || HasUsing(text, mapping.To))
            {
                continue;
            }

            text = InsertUsing(text, mapping.To);
        }

        return text;
    }

    private static string ApplyRenames(string value, IReadOnlyList<RenameRule> renames)
    {
        foreach (var rename in renames)
        {
            value = value.Replace(rename.From, rename.To, StringComparison.Ordinal);
        }

        return value;
    }

    private static bool HasNamespace(string text, string value) => Regex.IsMatch(
        text,
        $@"(?m)^\s*namespace\s+{Regex.Escape(value)}\s*[;{{]",
        RegexOptions.CultureInvariant);

    private static bool HasUsing(string text, string value) => Regex.IsMatch(
        text,
        $@"(?m)^\s*using\s+{Regex.Escape(value)}\s*;",
        RegexOptions.CultureInvariant);

    private static string InsertUsing(string text, string targetNamespace)
    {
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var usingMatches = Regex.Matches(
            text,
            @"(?m)^using\s+[^;]+;\r?$",
            RegexOptions.CultureInvariant);
        var usingLine = $"using {targetNamespace};{newLine}";

        if (usingMatches.Count > 0)
        {
            var last = usingMatches[^1];
            var insertionIndex = last.Index + last.Length;
            if (insertionIndex < text.Length && text[insertionIndex] == '\n')
            {
                insertionIndex++;
            }

            return text.Insert(insertionIndex, usingLine);
        }

        var namespaceMatch = Regex.Match(text, @"(?m)^\s*namespace\s+", RegexOptions.CultureInvariant);
        return namespaceMatch.Success
            ? text.Insert(namespaceMatch.Index, usingLine + newLine)
            : usingLine + newLine + text;
    }

    private static TextFormat? DetectFormat(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()))
        {
            return new TextFormat(new UTF8Encoding(true, true), Encoding.UTF8.GetPreamble().Length);
        }

        if (bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()))
        {
            return new TextFormat(new UnicodeEncoding(false, true, true), Encoding.Unicode.GetPreamble().Length);
        }

        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.GetPreamble()))
        {
            return new TextFormat(new UnicodeEncoding(true, true, true), Encoding.BigEndianUnicode.GetPreamble().Length);
        }

        try
        {
            _ = StrictUtf8.GetString(bytes);
            return new TextFormat(StrictUtf8, 0);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private sealed record TextFormat(Encoding Encoding, int PreambleLength);
}
