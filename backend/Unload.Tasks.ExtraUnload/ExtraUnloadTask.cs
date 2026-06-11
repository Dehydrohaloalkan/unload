using Microsoft.Extensions.Logging;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Задача дополнительной выгрузки (extra).
/// Deferred: стартует фоновую обработку через <see cref="ExtraActivationChannel"/> и возвращает Accepted.
/// Саму выгрузку выполняет фоновый воркер (<c>ExtraUnloadHostedService</c>) через <see cref="ExtraUnloadEngine"/>.
/// Проверку дневного окна и конфликтов делает <see cref="TaskWorkflow"/>.
/// <para>
/// Если выбраны все банки (<c>SelectedBanks</c> пуст) — выполняются базовые скрипты из <c>extra/</c>.
/// Если выбрано подмножество — выполняются <c>extra/atomic/</c> с подстановкой плейсхолдера <c>{banks}</c>.
/// </para>
/// </summary>
public class ExtraUnloadTask(
    ExtraUnloadOptions options,
    ExtraActivationChannel extraWorkflow,
    RunStateStore runStateStore,
    ILogger<ExtraUnloadTask> logger) : UnloadTask
{
    /// <summary>Плейсхолдер списка банков в atomic-скриптах: <c>WHERE NrBank IN ({banks})</c>.</summary>
    private const string BanksPlaceholder = "{banks}";

    private readonly ExtraUnloadOptions _options = options;
    private readonly ExtraActivationChannel _extraWorkflow = extraWorkflow;
    private readonly RunStateStore _runStateStore = runStateStore;
    private readonly ILogger<ExtraUnloadTask> _logger = logger;

    public override string Code => TaskCodes.Extra;

    public override IReadOnlyCollection<string> RequiresCompleted => [TaskCodes.Preset];

    public override IReadOnlyCollection<string> ConflictsWith => [TaskCodes.Preset];

    public override bool RequiresDailyWindowOpen => true;

    public override bool IsDeferred => true;

    public override async Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken)
    {
        var selectedBanks = NormalizeSelectedBanks(request.SelectedBanks);

        // null — «все банки» (базовые скрипты); явно переданный пустой набор — ошибка выбора,
        // иначе пользователь со снятыми галочками молча запустил бы выгрузку по всем банкам.
        if (request.SelectedBanks is not null && selectedBanks.Length == 0)
        {
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Validation,
                "EXTRA_NO_BANKS_SELECTED",
                "No banks selected for extra unload. Select at least one bank or run with all banks.");
        }

        var useAtomic = selectedBanks.Length > 0;
        var scriptsDirectory = useAtomic ? _options.AtomicScriptsDirectory : _options.ExtraScriptsDirectory;

        if (!Directory.Exists(scriptsDirectory))
        {
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Validation,
                "EXTRA_SCRIPTS_NOT_FOUND",
                $"Scripts directory was not found: {scriptsDirectory}");
        }

        var scriptPaths = Directory
            .EnumerateFiles(scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            // Файлы, начинающиеся с подчёркивания (напр. _banks.sql), — служебные, не data-скрипты.
            .Where(static path => !Path.GetFileName(path).StartsWith('_'))
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (scriptPaths.Length == 0)
        {
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Validation,
                "EXTRA_SCRIPTS_NOT_FOUND",
                $"No extra scripts found in '{scriptsDirectory}'.");
        }

        var banksFilter = useAtomic ? BuildBanksInClause(selectedBanks) : null;

        // Валидируем плейсхолдер заранее, чтобы вернуть понятную 400, а не «упасть» в фоне.
        if (useAtomic)
        {
            foreach (var path in scriptPaths)
            {
                var sql = await File.ReadAllTextAsync(path, cancellationToken);
                if (!sql.Contains(BanksPlaceholder, StringComparison.OrdinalIgnoreCase))
                {
                    throw new TaskLaunchException(
                        TaskLaunchFailureKind.Validation,
                        "EXTRA_PLACEHOLDER_MISSING",
                        $"Atomic script '{Path.GetFileNameWithoutExtension(path)}' does not contain required placeholder '{BanksPlaceholder}'.");
                }
            }
        }

        var correlationId = TaskCorrelationId.Create("extra");
        var scriptCodes = scriptPaths.Select(Path.GetFileNameWithoutExtension).ToArray();
        var payload = new ExtraRunRequest(correlationId, scriptPaths, banksFilter, request.PublishToGateway);

        if (!_extraWorkflow.TryActivate(correlationId, payload))
        {
            throw new TaskLaunchException(
                TaskLaunchFailureKind.Conflict,
                "TASK_ALREADY_RUNNING",
                "Extra task is already running.");
        }

        try
        {
            _runStateStore.SetStarted(
                correlationId,
                targetCodes: Array.Empty<string>(),
                memberNames: scriptCodes!,
                publishToGateway: request.PublishToGateway,
                taskCode: Code);
        }
        catch
        {
            _extraWorkflow.Complete(correlationId);
            throw;
        }

        _logger.LogInformation(
            "Extra task accepted. CorrelationId: {CorrelationId}, Scripts: {ScriptCount}, Atomic: {Atomic}, SelectedBanks: {BankCount}",
            correlationId,
            scriptPaths.Length,
            useAtomic,
            selectedBanks.Length);

        return new TaskExecutionResult(
            TaskCode: Code,
            ExecutionId: correlationId,
            Status: TaskExecutionStatus.Accepted,
            Message: "Extra accepted.");
    }

    private static string[] NormalizeSelectedBanks(IReadOnlyCollection<string>? selectedBanks)
    {
        if (selectedBanks is null)
        {
            return [];
        }

        return selectedBanks
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Select(static code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Строит подстановку для <c>{banks}</c>: строковые значения в одинарных кавычках через запятую,
    /// напр. <c>'B01','B02'</c>. Одинарные кавычки внутри значений экранируются удвоением.
    /// </summary>
    private static string BuildBanksInClause(IReadOnlyList<string> selectedBanks)
    {
        return string.Join(",", selectedBanks.Select(static code => $"'{code.Replace("'", "''")}'"));
    }
}
