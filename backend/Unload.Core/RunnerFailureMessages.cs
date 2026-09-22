namespace Unload.Core;

/// <summary>
/// Безопасные сообщения для публичного контракта runner-ошибок.
/// Текст исключения остаётся только в логах и никогда не отправляется клиенту.
/// </summary>
public static class RunnerFailureMessages
{
    public static RunnerEvent Sanitize(RunnerEvent @event)
    {
        if (@event.Failure is null)
        {
            return @event;
        }

        var failure = Sanitize(@event.Failure);
        return @event with
        {
            Message = failure.Message,
            Failure = failure
        };
    }

    public static RunnerFailureInfo Sanitize(RunnerFailureInfo failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure with
        {
            Message = ForStage(failure.Stage, failure.MemberName, failure.ScriptCode)
        };
    }

    public static string ForStage(
        string stage,
        string? memberName = null,
        string? scriptCode = null)
    {
        var scope = !string.IsNullOrWhiteSpace(scriptCode)
            ? $" for script '{scriptCode}'"
            : !string.IsNullOrWhiteSpace(memberName)
                ? $" for member '{memberName}'"
                : string.Empty;

        return stage switch
        {
            "database_connectivity" => "Database connectivity check failed. Verify database availability.",
            "output_directory" => "Output directory could not be prepared. Check server storage configuration.",
            "resolver" => "Target resolver failed. Check catalog configuration and server logs.",
            "query" => $"Query execution failed{scope}. Review the script and database logs.",
            "row_read" => $"Reading query rows failed{scope}. Review the database logs.",
            "file_write" => $"Output file could not be written{scope}. Check server storage permissions.",
            "gateway_publish" => $"Gateway publish failed{scope}. Check gateway service status.",
            "report_write" => "Run report could not be written. Check server storage permissions.",
            "background_worker" => "Run background worker failed. Check server logs for details.",
            "sender" => "Gateway sender failed. Check gateway service logs.",
            "cancellation" => "Run was cancelled.",
            _ => "Run failed. Check server logs for details."
        };
    }
}
