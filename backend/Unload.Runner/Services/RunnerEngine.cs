using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Unload.Core;

namespace Unload.Runner;

/// <summary>
/// Реализация движка выгрузки данных.
/// N worker-потоков, каждый с одним клиентом БД, обрабатывают мемберов. Файлы пишутся в один MQ.
/// </summary>
public class RunnerEngine : IRunner
{
    private const int EventChannelCapacity = 64;
    private readonly ICatalogService _catalogService;
    private readonly IDatabaseClientFactory _databaseClientFactory;
    private readonly IFileChunkWriter _fileChunkWriter;
    private readonly IMqPublisher _mqPublisher;
    private readonly RunnerOptions _options;

    public RunnerEngine(
        ICatalogService catalogService,
        IDatabaseClientFactory databaseClientFactory,
        IFileChunkWriter fileChunkWriter,
        IMqPublisher mqPublisher,
        RunnerOptions options)
    {
        RunnerEngineGuard.ValidateOptions(options);
        _catalogService = catalogService;
        _databaseClientFactory = databaseClientFactory;
        _fileChunkWriter = fileChunkWriter;
        _mqPublisher = mqPublisher;
        _options = options;
    }

    public IAsyncEnumerable<RunnerEvent> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<RunnerEvent>(new BoundedChannelOptions(EventChannelCapacity)
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
            RunnerEngineGuard.ValidateDatabaseConnectivity(_databaseClientFactory.CreateClient());

            runOutputDirectory = RunnerOutputDirectoryFactory.CreateRunOutputDirectory(request.OutputDirectory);
            var runFilesDirectory = RunnerOutputDirectoryFactory.CreateRunFilesDirectory(runOutputDirectory);
            eventEmitter = new RunnerEventEmitter(_mqPublisher, writer, request, cancellationToken);

            await eventEmitter.EmitAsync(RunnerStep.RequestAccepted, "Run request accepted.");

            var resolvedTargets = await _catalogService.ResolveAsync(request.TargetCodes, cancellationToken);
            await eventEmitter.EmitAsync(
                RunnerStep.TargetsResolved,
                $"Targets resolved: {resolvedTargets.Count}.",
                records: resolvedTargets.Count);

            var scripts = resolvedTargets
                .SelectMany(static x => x.Value)
                .OrderBy(static x => x.FirstCodeDigit)
                .ThenBy(static x => x.TargetCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static x => x.ScriptCode, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var script in scripts)
            {
                await eventEmitter.EmitForScriptAsync(script, RunnerStep.ScriptDiscovered, $"Discovered script {script.ScriptCode}.");
            }

            if (scripts.Length == 0)
            {
                await eventEmitter.EmitAsync(RunnerStep.Completed, "No scripts found for selected targets.");
                return;
            }

            var scriptsByMember = scripts
                .GroupBy(static x => x.MemberName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.ToArray(), StringComparer.OrdinalIgnoreCase);

            var memberQueue = new ConcurrentQueue<string>(scriptsByMember.Keys);
            var memberChunkCounters = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var workers = Enumerable.Range(1, _options.WorkerCount)
                .Select(workerId => RunWorkerAsync(
                    workerId,
                    memberQueue,
                    scriptsByMember,
                    runFilesDirectory,
                    eventEmitter,
                    reportRows,
                    memberChunkCounters,
                    cancellationToken))
                .ToArray();

            await Task.WhenAll(workers);

            await eventEmitter.EmitAsync(
                RunnerStep.Completed,
                $"Run completed successfully. Output: {runOutputDirectory}",
                filePath: runOutputDirectory);
        }
        catch (OperationCanceledException)
        {
            if (eventEmitter is not null)
                await eventEmitter.TryEmitFailureAsync(RunnerStep.Failed, "Run was cancelled.");
        }
        catch (Exception ex)
        {
            if (eventEmitter is not null)
                await eventEmitter.TryEmitFailureAsync(RunnerStep.Failed, ex.Message);
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

    private async Task RunWorkerAsync(
        int workerId,
        ConcurrentQueue<string> memberQueue,
        IReadOnlyDictionary<string, ScriptDefinition[]> scriptsByMember,
        string runFilesDirectory,
        RunnerEventEmitter eventEmitter,
        ConcurrentBag<RunReportRow> reportRows,
        ConcurrentDictionary<string, int> memberChunkCounters,
        CancellationToken cancellationToken)
    {
        var client = _databaseClientFactory.CreateClient();
        try
        {
            while (memberQueue.TryDequeue(out var memberName) && !cancellationToken.IsCancellationRequested)
            {
                var scripts = scriptsByMember[memberName];
                foreach (var script in scripts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ProcessScriptAsync(
                        script,
                        workerId,
                        client,
                        runFilesDirectory,
                        eventEmitter,
                        reportRows,
                        memberChunkCounters,
                        cancellationToken);
                }
            }
        }
        finally
        {
            if (client is IAsyncDisposable ad)
                await ad.DisposeAsync();
            else if (client is IDisposable d)
                d.Dispose();
        }
    }

    private async Task ProcessScriptAsync(
        ScriptDefinition script,
        int workerId,
        IDatabaseClient client,
        string runFilesDirectory,
        RunnerEventEmitter eventEmitter,
        ConcurrentBag<RunReportRow> reportRows,
        ConcurrentDictionary<string, int> memberChunkCounters,
        CancellationToken cancellationToken)
    {
        await eventEmitter.EmitForScriptAsync(
            script,
            RunnerStep.QueryStarted,
            $"Worker #{workerId} running query for script {script.ScriptCode}.");

        await using var reader = await client.GetDataReaderAsync(script.SqlText, cancellationToken);
        var columns = RunnerEngineDataReader.GetColumns(reader);

        if (columns.Count == 0)
            throw new InvalidOperationException($"Query for script '{script.ScriptCode}' returned no columns.");

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
                throw new InvalidOperationException($"Single row size {rowSize} bytes exceeds chunk size {_options.ChunkSizeBytes} bytes.");

            if (currentRows.Count > 0 && currentSize + rowSize > _options.ChunkSizeBytes)
            {
                var chunkNumber = memberChunkCounters.AddOrUpdate(script.MemberName, 1, static (_, c) => checked(c + 1));
                await WriteAndPublishChunkAsync(script, chunkNumber, currentRows.ToArray(), currentSize,
                    runFilesDirectory, eventEmitter, reportRows, cancellationToken);
                currentRows = [];
                currentSize = headerSize;
            }

            currentRows.Add(row);
            currentSize += rowSize;
            rowsRead++;
        }

        if (currentRows.Count > 0)
        {
            var chunkNumber = memberChunkCounters.AddOrUpdate(script.MemberName, 1, static (_, c) => checked(c + 1));
            await WriteAndPublishChunkAsync(
                script, chunkNumber, currentRows.ToArray(), currentSize,
                runFilesDirectory, eventEmitter, reportRows, cancellationToken);
        }
        else if (rowsRead == 0)
        {
            reportRows.Add(new RunReportRow(script.MemberName, script.ScriptType, script.FirstCodeDigit, string.Empty, 0, false, 0));
        }

        await eventEmitter.EmitForScriptAsync(
            script,
            RunnerStep.QueryCompleted,
            $"Worker #{workerId} finished query for script {script.ScriptCode}.",
            rowsRead);
    }

    private async Task WriteAndPublishChunkAsync(
        ScriptDefinition script,
        int chunkNumber,
        DatabaseRow[] rows,
        int byteSize,
        string runFilesDirectory,
        RunnerEventEmitter eventEmitter,
        ConcurrentBag<RunReportRow> reportRows,
        CancellationToken cancellationToken)
    {
        await eventEmitter.EmitForScriptAsync(script, RunnerStep.ChunkCreated, $"Chunk #{chunkNumber} created for {script.ScriptCode}.", rows.Length);

        var chunk = new FileChunk(script, chunkNumber, rows, byteSize);
        var stopwatch = Stopwatch.StartNew();
        var written = await _fileChunkWriter.WriteChunkAsync(chunk, runFilesDirectory, cancellationToken);
        stopwatch.Stop();

        var isSentToMq = await eventEmitter.EmitForScriptAsync(
            script,
            RunnerStep.FileWritten,
            $"File written: {Path.GetFileName(written.FilePath)}.",
            records: written.RowsCount,
            filePath: written.FilePath,
            awaitMqStatus: true,
            cancellationToken: cancellationToken);

        reportRows.Add(new RunReportRow(
            script.MemberName,
            script.ScriptType,
            script.FirstCodeDigit,
            Path.GetFileName(written.FilePath),
            written.RowsCount,
            isSentToMq,
            stopwatch.ElapsedMilliseconds));
    }
}
