using Unload.Api;
using Unload.Application;
using Unload.Core;

namespace Unload.WebConsole;

/// <summary>
/// Ответ API при конфликте запуска, когда уже есть активная выгрузка.
/// </summary>
internal record RunConflictResponse(string Message, string? ActiveCorrelationId);

/// <summary>
/// Результат попытки запуска: либо accepted, либо conflict.
/// </summary>
internal record RunStartResult(RunAcceptedResponse? Accepted, RunConflictResponse? Conflict);

/// <summary>
/// Строка события для отображения в live-таблице.
/// </summary>
internal record RunnerEventLine(DateTime Time, RunnerStep Step, string Message);

/// <summary>
/// Снимок UI-состояния для построения панели и ленты событий.
/// </summary>
internal record UiSnapshot(
    RunLifecycleStatus Status,
    RunnerStep? LastStep,
    string? Message,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<RunnerEventLine> Events,
    IReadOnlyList<MemberRunStatusInfo> Members,
    PresetGateState? PresetState);
