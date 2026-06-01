using Microsoft.Extensions.Logging;
using Unload.Store;
using Unload.Tasks;

namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Задача дополнительной выгрузки (extra).
/// Синхронная: выполняется полностью внутри <see cref="ExecuteAsync"/> и возвращает Completed.
/// Проверку дневного окна делает <see cref="TaskWorkflow"/> через <see cref="DailyWindowPolicy"/>.
/// <para>
/// Если выбраны все банки (<c>SelectedBanks</c> пуст) — выполняются базовые скрипты из <c>extra/</c>.
/// Если выбрано подмножество — выполняются <c>extra/atomic/</c> с подстановкой плейсхолдера <c>{banks}</c>.
/// </para>
/// </summary>
public class ExtraUnloadTask(
    ExtraUnloadOptions options,
    ExtraScriptExecutor scriptExecutor,
    ExtraOutputWriter outputWriter,
    TaskExecutionHistoryStore historyStore,
    ILogger<ExtraUnloadTask> logger) : UnloadTask
{
    /// <summary>Плейсхолдер списка банков в atomic-скриптах: <c>WHERE NrBank IN ({banks})</c>.</summary>
    private const string BanksPlaceholder = "{banks}";

    private readonly ExtraUnloadOptions _options = options;
    private readonly ExtraScriptExecutor _scriptExecutor = scriptExecutor;
    private readonly ExtraOutputWriter _outputWriter = outputWriter;
    private readonly TaskExecutionHistoryStore _historyStore = historyStore;
    private readonly ILogger<ExtraUnloadTask> _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public override string Code => TaskCodes.Extra;

    public override IReadOnlyCollection<string> RequiresCompleted => [TaskCodes.Preset];

    public override IReadOnlyCollection<string> ConflictsWith => [TaskCodes.Preset];

    public override bool RequiresDailyWindowOpen => true;

    public override async Task<TaskExecutionResult> ExecuteAsync(
        TaskLaunchRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _semaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogWarning("Extra task launch rejected: another extra task is already running.");
            throw new InvalidOperationException("Extra scripts task is already running.");
        }

        try
        {
            var selectedBanks = NormalizeSelectedBanks(request.SelectedBanks);
            var useAtomic = selectedBanks.Length > 0;
            var scriptsDirectory = useAtomic ? _options.AtomicScriptsDirectory : _options.ExtraScriptsDirectory;
            var correlationId = TaskCorrelationId.Create("extra");

            _logger.LogInformation(
                "Extra task started. CorrelationId: {CorrelationId}, ScriptsRoot: {ScriptsDirectory}, Atomic: {Atomic}, SelectedBanks: {BankCount}",
                correlationId,
                scriptsDirectory,
                useAtomic,
                selectedBanks.Length);

            if (!Directory.Exists(scriptsDirectory))
            {
                throw new DirectoryNotFoundException($"Scripts directory was not found: {scriptsDirectory}");
            }

            var scriptPaths = Directory
                .EnumerateFiles(scriptsDirectory, "*.sql", SearchOption.TopDirectoryOnly)
                // Файлы, начинающиеся с подчёркивания (напр. _banks.sql), — служебные, не data-скрипты.
                .Where(static path => !Path.GetFileName(path).StartsWith('_'))
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (scriptPaths.Length == 0)
            {
                _logger.LogInformation("Extra task finished with no scripts. CorrelationId: {CorrelationId}", correlationId);
                var emptyResult = new TaskExecutionResult(
                    TaskCode: Code,
                    ExecutionId: correlationId,
                    Status: TaskExecutionStatus.Completed,
                    Message: "No root scripts found.",
                    ScriptsExecuted: 0,
                    FilesWritten: 0,
                    OutputPath: null);

                RecordHistory(correlationId, emptyResult);
                return emptyResult;
            }

            var banksFilter = useAtomic ? BuildBanksInClause(selectedBanks) : null;
            var executionTasks = scriptPaths.Select(path =>
                ExecuteScriptAsync(path, banksFilter, correlationId, cancellationToken));
            var scriptResults = await Task.WhenAll(executionTasks);

            var writeResult = await _outputWriter.WriteAsync(
                _options.OutputDirectory,
                correlationId,
                scriptResults,
                request.PublishToGateway,
                cancellationToken);

            _logger.LogInformation(
                "Extra task completed. CorrelationId: {CorrelationId}, ScriptsExecuted: {ScriptsExecuted}, FilesWritten: {FilesWritten}, OutputPath: {OutputPath}",
                correlationId,
                scriptResults.Length,
                writeResult.FilesWritten,
                writeResult.OutputPath);

            var result = new TaskExecutionResult(
                TaskCode: Code,
                ExecutionId: correlationId,
                Status: TaskExecutionStatus.Completed,
                Message: "Extra scripts executed and files created.",
                ScriptsExecuted: scriptResults.Length,
                FilesWritten: writeResult.FilesWritten,
                OutputPath: writeResult.OutputPath);

            RecordHistory(correlationId, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Extra task failed.");
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<ExtraScriptExecutionResult> ExecuteScriptAsync(
        string scriptPath,
        string? banksFilter,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var scriptCode = Path.GetFileNameWithoutExtension(scriptPath);
        var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);

        if (banksFilter is not null)
        {
            if (!sql.Contains(BanksPlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Atomic script '{scriptCode}' does not contain required placeholder '{BanksPlaceholder}'.");
            }

            sql = sql.Replace(BanksPlaceholder, banksFilter, StringComparison.OrdinalIgnoreCase);
        }

        return await _scriptExecutor.ExecuteAsync(scriptCode, sql, correlationId, cancellationToken);
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

    private void RecordHistory(string correlationId, TaskExecutionResult result)
    {
        _historyStore.Add(
            Code,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            correlationId,
            result.Message,
            result.ScriptsExecuted,
            result.FilesWritten,
            result.OutputPath);
    }
}
