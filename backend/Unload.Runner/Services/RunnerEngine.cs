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
    private readonly SemaphoreSlim _databaseReaderGate = new(1, 1);

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
        RunnerEventEmitter? eventEmitter = null;

        try
        {
            RunnerEngineGuard.ValidateRequest(request);
            RunnerEngineGuard.ValidateDatabaseConnectivity(_databaseClient);

            runOutputDirectory = RunnerOutputDirectoryFactory.CreateRunOutputDirectory(request.OutputDirectory);
            var runFilesDirectory = RunnerOutputDirectoryFactory.CreateRunFilesDirectory(runOutputDirectory);
            eventEmitter = new RunnerEventEmitter(_mqPublisher, _options, writer, request, cancellationToken);

            await eventEmitter.EmitAsync(
                RunnerStep.RequestAccepted,
                "Run request accepted.");

            var resolvedTargets = await _catalogService.ResolveAsync(request.TargetCodes, cancellationToken);
            await eventEmitter.EmitAsync(
                RunnerStep.TargetsResolved,
                $"Targets resolved: {resolvedTargets.Count}.",
                records: resolvedTargets.Count);

            var scripts = resolvedTargets
                .SelectMany(static x => x.Value)
                .OrderBy(static x => x.TargetCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static x => x.ScriptCode, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var script in scripts)
            {
                await eventEmitter.EmitForScriptAsync(
                    script,
                    RunnerStep.ScriptDiscovered,
                    $"Discovered script {script.ScriptCode}.");
            }

            if (scripts.Length == 0)
            {
                await eventEmitter.EmitAsync(
                    RunnerStep.Completed,
                    "No scripts found for selected targets.");
                return;
            }

            await RunDataflowPipelineAsync(
                request,
                scripts,
                runFilesDirectory,
                eventEmitter,
                reportRows,
                cancellationToken);

            await eventEmitter.EmitAsync(
                RunnerStep.Completed,
                $"Run completed successfully. Output: {runOutputDirectory}",
                filePath: runOutputDirectory);
        }
        catch (OperationCanceledException)
        {
            if (eventEmitter is not null)
            {
                await eventEmitter.TryEmitFailureAsync(RunnerStep.Failed, "Run was cancelled.");
            }
        }
        catch (Exception ex)
        {
            if (eventEmitter is not null)
            {
                await eventEmitter.TryEmitFailureAsync(RunnerStep.Failed, ex.Message);
            }
        }
        finally
        {
            await (eventEmitter?.CompleteAsync() ?? Task.CompletedTask);

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
        RunnerEventEmitter eventEmitter,
        ConcurrentBag<RunReportRow> reportRows,
        CancellationToken cancellationToken)
    {
        using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pipelineToken = pipelineCts.Token;
        var memberChunkCounters = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var chunkQueue = new BufferBlock<ChunkWriteJob>(new DataflowBlockOptions
        {
            BoundedCapacity = _options.DataflowBoundedCapacity,
            CancellationToken = pipelineToken
        });

        var queryBlock = new ActionBlock<IReadOnlyList<ScriptDefinition>>(async memberScripts =>
        {
            foreach (var script in memberScripts)
            {
                await ProcessScriptQueryAsync(
                    script,
                    eventEmitter,
                    chunkQueue,
                    memberChunkCounters,
                    reportRows,
                    pipelineToken);
            }
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = _options.DataflowBoundedCapacity,
            MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism,
            CancellationToken = pipelineToken
        });

        var fileBlock = new TransformBlock<ChunkWriteJob, WrittenChunkJob>(async chunkJob =>
        {
            return await WriteChunkAsync(
                chunkJob,
                runFilesDirectory,
                eventEmitter,
                cancellationToken);
        }, new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = _options.DataflowBoundedCapacity,
            MaxDegreeOfParallelism = _options.FileWriterDegreeOfParallelism,
            CancellationToken = pipelineToken
        });

        var publishBlock = new ActionBlock<WrittenChunkJob>(async written =>
        {
            var isSentToMq = await eventEmitter.EmitForScriptAsync(
                written.Script,
                RunnerStep.FileWritten,
                $"File written: {Path.GetFileName(written.Written.FilePath)}.",
                records: written.Written.RowsCount,
                filePath: written.Written.FilePath,
                awaitMqStatus: true,
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

        var scriptsByMember = scripts
            .GroupBy(static x => x.MemberName, StringComparer.OrdinalIgnoreCase)
            .Select(static group => (IReadOnlyList<ScriptDefinition>)group.ToArray())
            .ToArray();

        foreach (var memberScripts in scriptsByMember)
        {
            var accepted = await queryBlock.SendAsync(memberScripts, pipelineToken);
            if (!accepted)
            {
                throw new InvalidOperationException("Query stage declined script message.");
            }
        }

        queryBlock.Complete();
        await publishBlock.Completion;
    }

    private async Task ProcessScriptQueryAsync(
        ScriptDefinition script,
        RunnerEventEmitter eventEmitter,
        ITargetBlock<ChunkWriteJob> chunkQueue,
        ConcurrentDictionary<string, int> memberChunkCounters,
        ConcurrentBag<RunReportRow> reportRows,
        CancellationToken cancellationToken)
    {
        await eventEmitter.EmitForScriptAsync(script, RunnerStep.QueryStarted, $"Running query for script {script.ScriptCode}.");

        await _databaseReaderGate.WaitAsync(cancellationToken);
        try
        {
            await using var reader = await _databaseClient.GetDataReaderAsync(script.SqlText, cancellationToken);
            var columns = RunnerEngineDataReader.GetColumns(reader);

            if (columns.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Query for script '{script.ScriptCode}' returned no columns.");
            }

            var rowsRead = 0;
            var currentRows = new List<DatabaseRow>();
            var dayOfYear = DateTimeOffset.Now.DayOfYear;
            var headerLine =
                $"#|{script.ScriptType}|{script.OutputFileStem}{dayOfYear:D3}{int.MaxValue}{script.OutputFileExtension}|{OutputFormatConstants.SenderCode}|{DateTimeOffset.Now:yyyy-MM-dd}|{int.MaxValue}|{script.FirstCodeDigit}";
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
                    var chunkNumber = GetNextChunkNumberForMember(script.MemberName, memberChunkCounters);
                    var accepted = await chunkQueue.SendAsync(
                        new ChunkWriteJob(script, chunkNumber, currentRows.ToArray(), currentSize),
                        cancellationToken);
                    if (!accepted)
                    {
                        throw new InvalidOperationException("File stage declined chunk message.");
                    }

                    currentRows = [];
                    currentSize = headerSize;
                }

                currentRows.Add(row);
                currentSize += rowSize;
                rowsRead++;
            }

            if (currentRows.Count > 0)
            {
                var chunkNumber = GetNextChunkNumberForMember(script.MemberName, memberChunkCounters);
                var accepted = await chunkQueue.SendAsync(
                    new ChunkWriteJob(script, chunkNumber, currentRows.ToArray(), currentSize),
                    cancellationToken);
                if (!accepted)
                {
                    throw new InvalidOperationException("File stage declined final chunk message.");
                }
            }
            else if (rowsRead == 0)
            {
                reportRows.Add(new RunReportRow(
                    script.MemberName,
                    script.ScriptType,
                    script.FirstCodeDigit,
                    string.Empty,
                    0,
                    false,
                    0));
            }

            await eventEmitter.EmitForScriptAsync(script, RunnerStep.QueryCompleted, $"Query finished for script {script.ScriptCode}.", rowsRead);
        }
        finally
        {
            _databaseReaderGate.Release();
        }
    }

    private async Task<WrittenChunkJob> WriteChunkAsync(
        ChunkWriteJob chunkJob,
        string runFilesDirectory,
        RunnerEventEmitter eventEmitter,
        CancellationToken cancellationToken)
    {
        await eventEmitter.EmitForScriptAsync(
            chunkJob.Script,
            RunnerStep.ChunkCreated,
            $"Chunk #{chunkJob.ChunkNumber} created for {chunkJob.Script.ScriptCode}.",
            chunkJob.Rows.Length);

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

    private sealed record ChunkWriteJob(
        ScriptDefinition Script,
        int ChunkNumber,
        DatabaseRow[] Rows,
        int ByteSize);

    private sealed record WrittenChunkJob(
        ScriptDefinition Script,
        WrittenFile Written,
        long ExecutionTimeMs);

    private static int GetNextChunkNumberForMember(
        string memberName,
        ConcurrentDictionary<string, int> memberChunkCounters)
    {
        return memberChunkCounters.AddOrUpdate(memberName, 1, static (_, current) => checked(current + 1));
    }

}
