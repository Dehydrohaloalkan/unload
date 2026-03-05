using System.Text.RegularExpressions;
using Spectre.Console;

namespace Unload.WebConsole;

internal sealed class AppOptions
{
    private static readonly Regex TargetCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);

    public required string ApiBaseUrl { get; init; }
    public required IReadOnlyCollection<string> TargetCodes { get; init; }

    public static AppOptions Parse(string[] args)
    {
        var apiBaseUrl = "http://localhost:5000";
        var targetsArgument = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--api", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                apiBaseUrl = args[++i].Trim();
                continue;
            }

            if (string.Equals(args[i], "--targets", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                targetsArgument = args[++i];
                continue;
            }
        }

        if (string.IsNullOrWhiteSpace(targetsArgument))
        {
            targetsArgument = AnsiConsole.Ask<string>(
                "Target codes comma separated (empty to watch active run):",
                string.Empty);
        }

        var targetCodes = targetsArgument
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static x => x.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var code in targetCodes)
        {
            if (!TargetCodePattern.IsMatch(code))
            {
                throw new InvalidOperationException($"Target code '{code}' is invalid.");
            }
        }

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("API url is invalid. Use absolute http/https URL.");
        }

        return new AppOptions
        {
            ApiBaseUrl = uri.ToString().TrimEnd('/'),
            TargetCodes = targetCodes
        };
    }
}
