using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Unload.Core;
using Unload.Tasks;

namespace Unload.Tasks.ExtraUnload;

/// <summary>
/// Движок extra-выгрузки. В отличие от <c>MainUnloadEngine</c>, все скрипты запускаются
/// <b>одновременно</b> — по задаче на скрипт в отдельном потоке пула, — а их события
/// (QueryStarted → FileWritten* → ScriptCompleted) сливаются в общий поток через
/// <see cref="Channel{T}"/>. В конце эмитится один Completed. Каждый скрипт проецируется как
/// «мембер» (MemberName = код скрипта), что даёт пер-скриптовый статус в UI. Исключения и отмена
/// пробрасываются наружу — их ловит хост-сервис.
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

    /// <summary>Выполняет все скрипты запроса параллельно и возвращает поток событий прогресса.</summary>
    public async IAsyncEnumerable<RunnerEvent> RunAsync(
        ExtraRunRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (runDirectory, filesDirectory) = _outputWriter.CreateRunDirectory(_options.OutputDirectory);
        _logger.LogInformation(
            "Extra run started (parallel). CorrelationId: {CorrelationId}, Scripts: {ScriptCount}, OutputPath: {OutputPath}",
            request.CorrelationId,
            request.ScriptPaths.Count,
            runDirectory);

        // Канал-«воронка»: каждая скрипт-задача пишет свои события, потребитель ниже их транслирует.
        var channel = Channel.CreateUnbounded<RunnerEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        var sequencer = new RunnerEventSequencer();
        var emitLock = new SemaphoreSlim(1, 1);

        async Task EmitAsync(RunnerEvent @event, CancellationToken token)
        {
            await emitLock.WaitAsync(token);
            try
            {
                var normalized = RunnerFailureMessages.Sanitize(@event);
                await channel.Writer.WriteAsync(normalized with { Sequence = sequencer.Next() }, token);
            }
            finally
            {
                emitLock.Release();
            }
        }

        // По задаче на скрипт. Task.Run уводит работу в пул, поэтому скрипты реально стартуют
        // одновременно (стаб-БД блокирует поток Thread.Sleep'ом). ct НЕ передаём в Task.Run, чтобы
        // отмена приходила как Faulted-исключение и доходила до потребителя через Complete(error).
        var scriptTasks = request.ScriptPaths
            .Select(scriptPath => Task.Run(
                () => ProcessScriptAsync(scriptPath, request, filesDirectory, EmitAsync, cancellationToken)))
            .ToArray();

        // Когда все скрипты завершатся — закрываем канал (с ошибкой, если хоть один упал).
        _ = Task.WhenAll(scriptTasks).ContinueWith(
            completed => channel.Writer.TryComplete(completed.Exception?.Flatten().InnerException),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // Трансляция событий. Если канал закрыт с ошибкой/отменён — ReadAllAsync пробросит её наружу.
        await foreach (var @event in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return @event;
        }

        // Сюда попадаем только при штатном завершении всех скриптов — суммируем файлы.
        var totalFiles = scriptTasks.Sum(static task => task.Result);

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
            FilePath: runDirectory,
            Sequence: sequencer.Next());
    }

    /// <summary>Выполняет один скрипт, пишет его события в <paramref name="writer"/> и возвращает число файлов.</summary>
    private async Task<int> ProcessScriptAsync(
        string scriptPath,
        ExtraRunRequest request,
        string filesDirectory,
        Func<RunnerEvent, CancellationToken, Task> emitAsync,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scriptCode = Path.GetFileNameWithoutExtension(scriptPath);
        var failureStage = "query";
        try
        {
            await emitAsync(
                Event(request, RunnerStep.QueryStarted, scriptCode, $"Выполняется скрипт {scriptCode}."),
                cancellationToken);

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

            failureStage = "query";
            var execResult = await _scriptExecutor.ExecuteAsync(scriptCode, sql, request.CorrelationId, cancellationToken);
            failureStage = "file_write";
            var writeResult = await _outputWriter.WriteScriptAsync(
                filesDirectory, request.CorrelationId, execResult, request.PublishToGateway, cancellationToken);

            foreach (var file in writeResult.Files)
            {
                await emitAsync(
                    Event(request, RunnerStep.FileWritten, scriptCode, file.FileName, filePath: file.FilePath),
                    cancellationToken);
            }

            // 0 файлов — явно сообщаем «выполнено, 0 файлов».
            var completedMessage = writeResult.FilesWritten == 0
                ? $"Скрипт {scriptCode} выполнен, 0 файлов."
                : $"Скрипт {scriptCode} выполнен: файлов {writeResult.FilesWritten}, строк {execResult.Records}.";
            await emitAsync(
                Event(request, RunnerStep.ScriptCompleted, scriptCode, completedMessage, records: execResult.Records),
                cancellationToken);

            return writeResult.FilesWritten;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryEmitFailureAsync(
                emitAsync,
                Event(
                    request,
                    RunnerStep.Failed,
                    scriptCode,
                    "Extra run was cancelled.",
                    filePath: null) with
                {
                    Failure = CreateFailure(
                        failureStage,
                        "EXTRA_CANCELLED",
                        "Extra run was cancelled.",
                        request,
                        scriptCode)
                });
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Extra script failed. CorrelationId: {CorrelationId}, Script: {ScriptCode}, Stage: {Stage}",
                request.CorrelationId,
                scriptCode,
                failureStage);
            var safeMessage = RunnerFailureMessages.ForStage(
                failureStage,
                scriptCode: scriptCode);
            await TryEmitFailureAsync(
                emitAsync,
                Event(request, RunnerStep.Failed, scriptCode, safeMessage) with
                {
                    Failure = CreateFailure(
                        failureStage,
                        failureStage == "file_write" ? "EXTRA_FILE_WRITE_FAILED" : "EXTRA_QUERY_FAILED",
                        safeMessage,
                        request,
                        scriptCode)
                });
            throw;
        }
    }

    private static async Task TryEmitFailureAsync(
        Func<RunnerEvent, CancellationToken, Task> emitAsync,
        RunnerEvent @event)
    {
        try
        {
            await emitAsync(@event, CancellationToken.None);
        }
        catch
        {
        }
    }

    private static RunnerFailureInfo CreateFailure(
        string stage,
        string code,
        string message,
        ExtraRunRequest request,
        string scriptCode) => new(
        stage,
        "script",
        scriptCode,
        scriptCode,
        scriptCode,
        null,
        null,
        null,
        null,
        code,
        message,
        DateTimeOffset.UtcNow);

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
