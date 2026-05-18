using Unload.Tasks;

namespace Unload.Api.ErrorHandling;

/// <summary>
/// Устаревший маппер исключений. Используйте <see cref="TaskLaunchExceptions"/>.
/// Сохранён для совместимости; удалится в Фазе 6.
/// </summary>
public static class WorkflowDispatchExceptions
{
    public static ApiProblemException ToApiProblem(
        TaskLaunchException exception,
        string conflictTitle)
    {
        return TaskLaunchExceptions.ToApiProblem(exception, conflictTitle);
    }
}
