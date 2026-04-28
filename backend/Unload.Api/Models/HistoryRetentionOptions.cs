namespace Unload.Api.Models;

public sealed record HistoryRetentionOptions(
    int RetentionDays,
    int PruneIntervalMinutes)
{
    public const string SectionName = "HistoryRetention";

    public static readonly HistoryRetentionOptions Default = new(
        RetentionDays: 14,
        PruneIntervalMinutes: 60);
}

