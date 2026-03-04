using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using Unload.Core;

namespace Unload.Runner;

/// <summary>
/// Реализация движка выгрузки данных.
/// Используется background worker и console для выполнения SQL-скриптов, чанкования результатов и публикации событий.
/// </summary>
public class RunnerEngine : IRunner
{
    private readonly ICatalogService _catalogService;
    private readonly IDatabaseClient _databaseClient;
    private readonly IFileChunkWriter _fileChunkWriter;
    private readonly IMqPublisher _mqPublisher;
    private readonly RunnerOptions _options;

    /// <summary>
    /// Создает экземпляр раннера с инфраструктурными зависимостями.
    /// </summary>
    /// <param name="catalogService">Сервис резолва target-кодов и скриптов.</param>
    /// <param name="databaseClient">Клиент чтения данных из БД.</param>
    /// <param name="fileChunkWriter">Сервис записи чанков в файлы.</param>
    /// <param name="mqPublisher">Публикатор событий раннера в MQ.</param>
    /// <param name="options">Опции чанкования и параллелизма.</param>
    public RunnerEngine(
        ICatalogService catalogService,
        IDatabaseClient databaseClient,
        IFileChunkWriter fileChunkWriter,
        IMqPublisher mqPublisher,
        RunnerOptions options)
    {
        RunnerEngineGuard.ValidateOptions(options);
        _catalogService = catalogService;
        _databaseClient = databaseClient;
        _fileChunkWriter = fileChunkWriter;
        _mqPublisher = mqPublisher;
        _options = options;
    }

    /// <summary>
    /// Запускает обработку запроса и отдает поток событий выполнения.
    /// </summary>
    /// <param name="request">Параметры запуска выгрузки.</param>
    /// <param name="cancellationToken">Токен отмены выполнения.</param>
    /// <returns>Асинхронный поток событий раннера.</returns>
    public IAsyncEnumerable<RunnerEvent> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<RunnerEvent>(new BoundedChannelOptions(_options.DataflowBoundedCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecutePipelineAsync(request, channel.Writer, cancellationToken);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Выполняет полный пайплайн выгрузки: резолв target-кодов, обработка скриптов и финализация запуска.
    /// </summary>
    /// <param name="request">Запрос запуска.</param>
    /// <param name="writer">Канал, в который пишутся события выполнения.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task ExecutePipelineAsync(
        RunRequest request,
        ChannelWriter<RunnerEvent> writer,
        CancellationToken cancellationToken)
    {
        string? runOutputDirectory = null;
        var reportRows = new ConcurrentBag<RunReportRow>();
        ActionBlock<PendingRunnerEvent>? eventBlock = null;

        try
        {
            RunnerEngineGuard.ValidateRequest(request);
            RunnerEngineGuard.ValidateDatabaseConnectivity(_databaseClient);

            runOutputDirectory = RunnerOutputDirectoryFactory.CreateRunOutputDirectory(request.OutputDirectory);
            var runFilesDirectory = RunnerOutputDirectoryFactory.CreateRunFilesDirectory(runOutputDirectory);
            eventBlock = CreateEventBlock(writer, cancellationToken);

            await EmitViaDataflowAsync(
                eventBlock,
                writer,
                request,
                RunnerStep.RequestAccepted,
                "Run request accepted.",
                cancellationToken: cancellationToken);

            var resolvedTargets = await _catalogService.ResolveAsync(request.TargetCodes, cancellationToken);
            await EmitViaDataflowAsync(
                eventBlock,
                writer,
                request,
                RunnerStep.TargetsResolved,
                $"Targets resolved: {resolvedTargets.Count}.",
                records: resolvedTargets.Count,
                cancellationToken: cancellationToken);

            var scripts = resolvedTargets
                .SelectMany(static x => x.Value)
                .OrderBy(static x => x.TargetCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static x => x.ScriptCode, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var script in scripts)
            {
                await EmitViaDataflowAsync(
                    eventBlock,
                    writer,
                    request,
                    RunnerStep.ScriptDiscovered,
                    $"Discovered script {script.ScriptCode}.",
                    targetCode: script.TargetCode,
                    scriptCode: script.ScriptCode,
                    cancellationToken: cancellationToken);
            }

            if (scripts.Length == 0)
            {
                await EmitViaDataflowAsync(
                    eventBlock,
                    writer,
                    request,
                    RunnerStep.Completed,
                    "No scripts found for selected targets.",
                    cancellationToken: cancellationToken);
                return;
            }

            await RunDataflowPipelineAsync(
                request,
                scripts,
                runFilesDirectory,
                writer,
                eventBlock,
                reportRows,
                cancellationToken);

            await EmitViaDataflowAsync(
                eventBlock,
                writer,
                request,
                RunnerStep.Completed,
                $"Run completed successfully. Output: {runOutputDirectory}",
                filePath: runOutputDirectory,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (eventBlock is not null)
            {
                await TryEmitFailureAsync(
                    eventBlock,
                    writer,
                    request,
                    RunnerStep.Failed,
                    "Run was cancelled.");
            }
        }
        catch (Exception ex)
        {
            if (eventBlock is not null)
            {
                await TryEmitFailureAsync(
                    eventBlock,
                    writer,
                    request,
                    RunnerStep.Failed,
                    ex.Message);
            }
        }
        finally
        {
            if (eventBlock is not null)
            {
                eventBlock.Complete();
                try
                {
                    await eventBlock.Completion;
                }
                catch
                {
                }
            }

            if (runOutputDirectory is not null)
            {
                var reportPath = Path.Combine(runOutputDirectory, RunnerOutputDirectoryFactory.RunReportFileName);
                await RunReportCsvWriter.WriteAsync(reportPath, reportRows, CancellationToken.None);
            }
        }
    }

    private async Task RunDataflowPipelineAsync(
        RunRequest request,
        IReadOnlyList<ScriptDefinition> scripts,
        string runFilesDirectory,
        ChannelWriter<RunnerEvent> writer,
        ActionBlock<PendingRunnerEvent> eventBlock,
        ConcurrentBag<RunReportRow> reportRows,
        CancellationToken cancellationToken)
    {
        using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pipelineToken = pipelineCts.Token;

        var chunkQueue = new BufferBlock<ChunkWriteJob>(new DataflowBlockOptions
        {
            BoundedCapacity = _options.DataflowBoundedCapacity,
            CancellationToken = pipelineToken
        });

        var queryBlock = new ActionBlock<ScriptDefinition>(async script =>
        {
            await ProcessScriptQueryAsync(
                request,
                script,
                writer,
                eventBlock,
                chunkQueue,
                pipelineToken);
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = _options.DataflowBoundedCapacity,
            MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism,
            CancellationToken = pipelineToken
        });

        var fileBlock = new TransformBlock<ChunkWriteJob, WrittenChunkJob>(async chunkJob =>
        {
            return await WriteChunkAsync(
                request,
                chunkJob,
                runFilesDirectory,
                writer,
                eventBlock,
                cancellationToken);
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = _options.DataflowBoundedCapacity,
            MaxDegreeOfParallelism = _options.FileWriterDegreeOfParallelism,
            CancellationToken = pipelineToken
        });

        var publishBlock = new ActionBlock<WrittenChunkJob>(async written =>
        {
            var isSentToMq = await EmitViaDataflowAsync(
                eventBlock,
                writer,
                request,
                RunnerStep.FileWritten,
                $"File written: {Path.GetFileName(written.Written.FilePath)}.",
                targetCode: written.Script.TargetCode,
                scriptCode: written.Script.ScriptCode,
                records: written.Written.RowsCount,
                filePath: written.Written.FilePath,
                cancellationToken: pipelineToken);

            reportRows.Add(new RunReportRow(
                written.Script.MemberName,
                written.Script.ScriptType,
                written.Script.FirstCodeDigit,
                Path.GetFileName(written.Written.FilePath),
                written.Written.RowsCount,
                isSentToMq,
                written.ExecutionTimeMs));
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = _options.DataflowBoundedCapacity,
            MaxDegreeOfParallelism = _options.QueuePublisherDegreeOfParallelism,
            CancellationToken = pipelineToken
        });

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        chunkQueue.LinkTo(fileBlock, linkOptions);
        fileBlock.LinkTo(publishBlock, linkOptions);

        _ = queryBlock.Completion.ContinueWith(task =>
        {
            if (task.IsFaulted && task.Exception is not null)
            {
                ((IDataflowBlock)chunkQueue).Fault(task.Exception.GetBaseException());
                pipelineCts.Cancel();
            }
            else if (task.IsCanceled)
            {
                ((IDataflowBlock)chunkQueue).Fault(new OperationCanceledException(pipelineToken));
                pipelineCts.Cancel();
            }
            else
            {
                chunkQueue.Complete();
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        _ = fileBlock.Completion.ContinueWith(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                pipelineCts.Cancel();
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        _ = publishBlock.Completion.ContinueWith(task =>
        {
            if (task.IsFaulted || task.IsCanceled)
            {
                pipelineCts.Cancel();
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        foreach (var script in scripts)
        {
            var accepted = await queryBlock.SendAsync(script, pipelineToken);
            if (!accepted)
            {
                throw new InvalidOperationException("Query stage declined script message.");
            }
        }

        queryBlock.Complete();
        await publishBlock.Completion;
    }

    private async Task ProcessScriptQueryAsync(
        RunRequest request,
        ScriptDefinition script,
        ChannelWriter<RunnerEvent> writer,
        ActionBlock<PendingRunnerEvent> eventBlock,
        ITargetBlock<ChunkWriteJob> chunkQueue,
        CancellationToken cancellationToken)
    {
        await EmitViaDataflowAsync(
            eventBlock,
            writer,
            request,
            RunnerStep.QueryStarted,
            $"Running query for script {script.ScriptCode}.",
            targetCode: script.TargetCode,
            scriptCode: script.ScriptCode,
            cancellationToken: cancellationToken);

        await using var reader = await _databaseClient.GetDataReaderAsync(script.SqlText, cancellationToken);
        var columns = RunnerEngineDataReader.GetColumns(reader);

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Query for script '{script.ScriptCode}' returned no columns.");
        }

        var rowsRead = 0;
        var chunkNumber = 1;
        var currentRows = new List<DatabaseRow>();
        var headerLine =
            $"#|{script.ScriptType}|{script.OutputFileStem}{DateTimeOffset.Now.DayOfYear}{chunkNumber}{script.OutputFileExtension}|{OutputFormatConstants.SenderCode}|{DateTimeOffset.Now:yyyy-MM-dd}|{int.MaxValue}|{script.FirstCodeDigit}";
        var headerSize = PipeDelimitedFormatter.EstimateLineBytes(headerLine);
        var currentSize = headerSize;

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = RunnerEngineDataReader.ReadRow(reader, columns);
            var line = PipeDelimitedFormatter.BuildDataLine(row, columns);
            var rowSize = PipeDelimitedFormatter.EstimateLineBytes(line);
            if (rowSize + headerSize > _options.ChunkSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Single row size {rowSize} bytes exceeds chunk size {_options.ChunkSizeBytes} bytes.");
            }

            if (currentRows.Count > 0 && currentSize + rowSize > _options.ChunkSizeBytes)
            {
                var accepted = await chunkQueue.SendAsync(
                    new ChunkWriteJob(script, chunkNumber, currentRows.ToArray(), currentSize),
                    cancellationToken);
                if (!accepted)
                {
                    throw new InvalidOperationException("File stage declined chunk message.");
                }

                chunkNumber++;
                currentRows = [];
                currentSize = headerSize;
            }

            currentRows.Add(row);
            currentSize += rowSize;
            rowsRead++;
        }

        if (currentRows.Count > 0)
        {
            var accepted = await chunkQueue.SendAsync(
                new ChunkWriteJob(script, chunkNumber, currentRows.ToArray(), currentSize),
                cancellationToken);
            if (!accepted)
            {
                throw new InvalidOperationException("File stage declined final chunk message.");
            }
        }

        await EmitViaDataflowAsync(
            eventBlock,
            writer,
            request,
            RunnerStep.QueryCompleted,
            $"Query finished for script {script.ScriptCode}.",
            targetCode: script.TargetCode,
            scriptCode: script.ScriptCode,
            records: rowsRead,
            cancellationToken: cancellationToken);
    }

    private async Task<WrittenChunkJob> WriteChunkAsync(
        RunRequest request,
        ChunkWriteJob chunkJob,
        string runFilesDirectory,
        ChannelWriter<RunnerEvent> writer,
        ActionBlock<PendingRunnerEvent> eventBlock,
        CancellationToken cancellationToken)
    {
        await EmitViaDataflowAsync(
            eventBlock,
            writer,
            request,
            RunnerStep.ChunkCreated,
            $"Chunk #{chunkJob.ChunkNumber} created for {chunkJob.Script.ScriptCode}.",
            targetCode: chunkJob.Script.TargetCode,
            scriptCode: chunkJob.Script.ScriptCode,
            records: chunkJob.Rows.Length,
            cancellationToken: cancellationToken);

        var chunk = new FileChunk(
            chunkJob.Script,
            chunkJob.ChunkNumber,
            chunkJob.Rows,
            chunkJob.ByteSize);
        var stopwatch = Stopwatch.StartNew();
        var written = await _fileChunkWriter.WriteChunkAsync(chunk, runFilesDirectory, cancellationToken);
        stopwatch.Stop();
        return new WrittenChunkJob(chunkJob.Script, written, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Создает событие раннера и доставляет его в MQ и поток клиентских событий.
    /// </summary>
    /// <param name="writer">Канал публикации событий раннера.</param>
    /// <param name="request">Запрос запуска.</param>
    /// <param name="step">Шаг выполнения.</param>
    /// <param name="message">Текст события.</param>
    /// <param name="targetCode">Target-код (опционально).</param>
    /// <param name="scriptCode">Код скрипта (опционально).</param>
    /// <param name="records">Количество записей (опционально).</param>
    /// <param name="filePath">Путь к файлу результата (опционально).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    private async Task<bool> EmitViaDataflowAsync(
        ActionBlock<PendingRunnerEvent> eventBlock,
        ChannelWriter<RunnerEvent> writer,
        RunRequest request,
        RunnerStep step,
        string message,
        string? targetCode = null,
        string? scriptCode = null,
        int? records = null,
        string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        var @event = new RunnerEvent(
            DateTimeOffset.UtcNow,
            request.CorrelationId,
            step,
            message,
            targetCode,
            scriptCode,
            records,
            filePath);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = await eventBlock.SendAsync(new PendingRunnerEvent(@event, completion), cancellationToken);
        if (!accepted)
        {
            throw new InvalidOperationException("Event stage declined event message.");
        }

        return await completion.Task;
    }

    private ActionBlock<PendingRunnerEvent> CreateEventBlock(
        ChannelWriter<RunnerEvent> writer,
        CancellationToken cancellationToken)
    {
        return new ActionBlock<PendingRunnerEvent>(async pending =>
        {
            try
            {
                var isSentToMq = await TryPublishToMqAsync(pending.Event, cancellationToken);
                await writer.WriteAsync(pending.Event, cancellationToken);
                pending.Completion.TrySetResult(isSentToMq);
            }
            catch (Exception ex)
            {
                pending.Completion.TrySetException(ex);
                throw;
            }
        }, new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = _options.QueuePublisherDegreeOfParallelism,
            BoundedCapacity = _options.DataflowBoundedCapacity,
            CancellationToken = cancellationToken
        });
    }

    private async Task TryEmitFailureAsync(
        ActionBlock<PendingRunnerEvent> eventBlock,
        ChannelWriter<RunnerEvent> writer,
        RunRequest request,
        RunnerStep step,
        string message)
    {
        try
        {
            await EmitViaDataflowAsync(
                eventBlock,
                writer,
                request,
                step,
                message,
                cancellationToken: CancellationToken.None);
        }
        catch
        {
        }
    }

    private async Task<bool> TryPublishToMqAsync(RunnerEvent @event, CancellationToken cancellationToken)
    {
        try
        {
            await _mqPublisher.PublishAsync(@event, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private sealed record ChunkWriteJob(
        ScriptDefinition Script,
        int ChunkNumber,
        DatabaseRow[] Rows,
        int ByteSize);

    private sealed record WrittenChunkJob(
        ScriptDefinition Script,
        WrittenFile Written,
        long ExecutionTimeMs);

    private sealed record PendingRunnerEvent(
        RunnerEvent Event,
        TaskCompletionSource<bool> Completion);

}
