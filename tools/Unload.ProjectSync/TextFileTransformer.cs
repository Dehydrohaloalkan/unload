using System.Text;

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
