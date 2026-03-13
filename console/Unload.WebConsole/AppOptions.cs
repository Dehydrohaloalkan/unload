using System.Text.RegularExpressions;
namespace Unload.WebConsole;

/// <summary>
/// Параметры запуска web-консоли и их валидация.
/// </summary>
internal sealed class AppOptions
{
    private static readonly Regex TargetCodePattern = new("^[A-Z0-9_]{3,64}$", RegexOptions.Compiled);

    /// <summary>
    /// Базовый URL API для HTTP и SignalR-подключений.
    /// </summary>
    public required string ApiBaseUrl { get; init; }
    /// <summary>
    /// Нормализованный список кодов мемберов для запуска.
    /// </summary>
    public required IReadOnlyCollection<string> MemberCodes { get; init; }
    /// <summary>
    /// Запустить preset-задачу и завершить приложение.
    /// </summary>
    public bool RunPresetTask { get; init; }
    /// <summary>
    /// Запустить extra-задачу и завершить приложение.
    /// </summary>
    public bool RunExtraTask { get; init; }

    /// <summary>
    /// Разбирает аргументы командной строки и возвращает валидированные параметры приложения.
    /// </summary>
    /// <param name="args">Аргументы запуска процесса.</param>
    /// <returns>Готовые параметры web-консоли.</returns>
    public static AppOptions Parse(string[] args)
    {
        var apiBaseUrl = "http://localhost:5000";
        var membersArgument = string.Empty;
        var runPreset = false;
        var runExtra = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--api", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                apiBaseUrl = args[++i].Trim();
                continue;
            }

            if (string.Equals(args[i], "--members", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                membersArgument = args[++i];
                continue;
            }

            if (string.Equals(args[i], "--preset", StringComparison.OrdinalIgnoreCase))
            {
                runPreset = true;
                continue;
            }

            if (string.Equals(args[i], "--extra", StringComparison.OrdinalIgnoreCase))
            {
                runExtra = true;
            }
        }

        var memberCodes = string.IsNullOrWhiteSpace(membersArgument)
            ? Array.Empty<string>()
            : membersArgument
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(static x => x.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        foreach (var code in memberCodes)
        {
            if (!TargetCodePattern.IsMatch(code))
            {
                throw new InvalidOperationException($"Member code '{code}' is invalid.");
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
            MemberCodes = memberCodes,
            RunPresetTask = runPreset,
            RunExtraTask = runExtra
        };
    }
}
