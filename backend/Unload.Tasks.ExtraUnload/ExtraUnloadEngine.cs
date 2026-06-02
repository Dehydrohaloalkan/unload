using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Unload.Core;
using Unload.Tasks;

namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Движок extra-выгрузки. Аналог <c>MainUnloadEngine</c>: выполняет скрипты по одному и эмитит
/// поток <see cref="RunnerEvent"/> (по скрипту: QueryStarted → FileWritten* → ScriptCompleted,
/// в конце — Completed). Каждый скрипт проецируется как «мембер» (MemberName = код скрипта),
/// что даёт пер-скриптовый статус в UI. Исключения пробрасываются наружу — их ловит хост-сервис.
/// </summary>
public class ExtraUnloadEngine(
    ExtraUnloadOptions options,
    ExtraScriptExecutor scriptExecutor,
    ExtraOutputWriter outputWriter,
    ILogger<ExtraUnloadEngine> logger)
{
    /// <summary>Плейсхолдер списка банков в atomic-скриптах: <c>WHERE NrBank IN ({banks})</c>.</summary>
    private const string BanksPlaceholder = "{banks}";

    private readonly ExtraUnloadOptions _options = options;
    private readonly ExtraScriptExecutor _scriptExecutor = scriptExecutor;
    private readonly ExtraOutputWriter _outputWriter = outputWriter;
    private readonly ILogger<ExtraUnloadEngine> _logger = logger;

    /// <summary>Выполняет все скрипты запроса и возвращает поток событий прогресса.</summary>
    public async IAsyncEnumerable<RunnerEvent> RunAsync(
        ExtraRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (runDirectory, filesDirectory) = _outputWriter.CreateRunDirectory(_options.OutputDirectory);
        _logger.LogInformation(
            "Extra run started. CorrelationId: {CorrelationId}, Scripts: {ScriptCount}, OutputPath: {OutputPath}",
            request.CorrelationId,
            request.ScriptPaths.Count,
            runDirectory);

        var totalFiles = 0;

        foreach (var scriptPath in request.ScriptPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scriptCode = Path.GetFileNameWithoutExtension(scriptPath);

            yield return Event(request, RunnerStep.QueryStarted, scriptCode,
                $"Выполняется скрипт {scriptCode}.");

            var sql = await File.ReadAllTextAsync(scriptPath, cancellationToken);
            if (request.BanksFilter is not null)
            {
                if (!sql.Contains(BanksPlaceholder, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Atomic script '{scriptCode}' does not contain required placeholder '{BanksPlaceholder}'.");
                }

                sql = sql.Replace(BanksPlaceholder, request.BanksFilter, StringComparison.OrdinalIgnoreCase);
            }

            var execResult = await _scriptExecutor.ExecuteAsync(scriptCode, sql, request.CorrelationId, cancellationToken);
            var writeResult = await _outputWriter.WriteScriptAsync(
                filesDirectory, request.CorrelationId, execResult, request.PublishToGateway, cancellationToken);

            foreach (var file in writeResult.Files)
            {
                yield return Event(request, RunnerStep.FileWritten, scriptCode, file.FileName,
                    filePath: file.FilePath);
            }

            totalFiles += writeResult.FilesWritten;
            // #5: 0 файлов — явно сообщаем «выполнено, 0 файлов», а не просто «завершён».
            var completedMessage = writeResult.FilesWritten == 0
                ? $"Скрипт {scriptCode} выполнен, 0 файлов."
                : $"Скрипт {scriptCode} выполнен: файлов {writeResult.FilesWritten}, строк {execResult.Records}.";
            yield return Event(request, RunnerStep.ScriptCompleted, scriptCode, completedMessage,
                records: execResult.Records);
        }

        _logger.LogInformation(
            "Extra run finished. CorrelationId: {CorrelationId}, FilesWritten: {FilesWritten}",
            request.CorrelationId,
            totalFiles);

        yield return new RunnerEvent(
            OccurredAt: DateTimeOffset.UtcNow,
            CorrelationId: request.CorrelationId,
            Step: RunnerStep.Completed,
            Message: totalFiles == 0
                ? "Доп-выгрузка завершена, 0 файлов."
                : $"Доп-выгрузка завершена. Файлов: {totalFiles}.",
            FilePath: runDirectory);
    }

    private static RunnerEvent Event(
        ExtraRunRequest request,
        RunnerStep step,
        string scriptCode,
        string message,
        int? records = null,
        string? filePath = null)
    {
        return new RunnerEvent(
            OccurredAt: DateTimeOffset.UtcNow,
            CorrelationId: request.CorrelationId,
            Step: step,
            Message: message,
            MemberName: scriptCode,
            ScriptCode: scriptCode,
            Records: records,
            FilePath: filePath);
    }
}
