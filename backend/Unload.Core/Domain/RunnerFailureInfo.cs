namespace Unload.Core;

/// <summary>
/// Машиночитаемая диагностика ошибки pipeline с максимально доступной областью действия.
/// Не содержит SQL, секретов или stack trace, пригодных только для внутреннего журнала.
/// </summary>
public record RunnerFailureInfo(
    string Stage,
    string EntityType,
    string? EntityId,
    string? MemberName,
    string? ScriptCode,
    int? WorkerId,
    int? ChunkNumber,
    string? FilePath,
    string? BatchId,
    string Code,
    string Message,
    DateTimeOffset OccurredAt);
