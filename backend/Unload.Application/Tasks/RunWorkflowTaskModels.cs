namespace Unload.Application;

/// <summary>
/// Режим интерпретации входных кодов запуска.
/// </summary>
public enum RunSelectionMode
{
    MemberCodes,
    TargetCodes
}

/// <summary>
/// Запрос задачи запуска выгрузки.
/// </summary>
/// <param name="Codes">Входные коды выбора.</param>
/// <param name="SelectionMode">Режим интерпретации кодов.</param>
public sealed record StartRunTaskRequest(
    IReadOnlyCollection<string> Codes,
    RunSelectionMode SelectionMode);

/// <summary>
/// Результат принятого запуска выгрузки.
/// </summary>
/// <param name="CorrelationId">Идентификатор принятого запуска.</param>
public sealed record StartRunTaskResult(string CorrelationId);

/// <summary>
/// Пустой запрос для задач без payload.
/// </summary>
public sealed record EmptyWorkflowTaskRequest;
