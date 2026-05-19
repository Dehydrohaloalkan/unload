using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Unload.Core;
using Unload.Tasks.MainUnload.Models;

namespace Unload.Tasks.MainUnload;

/// <summary>
/// Движок основной выгрузки данных.
/// N worker-потоков (n-1 для больших скриптов, 1 для легких), каждый с одним клиентом БД.
/// </summary>
public class MainUnloadEngine
{
    private const int EventChannelCapacity = 64;
    private readonly ICatalogService _catalogService;
    private readonly IDatabaseClientFactory _databaseClientFactory;
    private readonly IFileChunkWriter _fileChunkWriter;
    private readonly IGatewayPublisher _gatewayPublisher;
    private readonly RunnerOptions _options;

    public MainUnloadEngine(
        ICatalogService catalogService,
        IDatabaseClientFactory databaseClientFactory,
        IFileChunkWriter fileChunkWriter,
        IGatewayPublisher gatewayPublisher,
        RunnerOptions options)
    {
        RunnerEngineGuard.ValidateOptions(options);
        _catalogService = catalogService;
        _databaseClientFactory = databaseClientFactory;
        _fileChunkWriter = fileChunkWriter;
        _gatewayPublisher = gatewayPublisher;
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
        var senderBatchBuilder = new SenderBatchBuilder();
        RunnerEventEmitter? eventEmitter = null;

        try
        {
            RunnerEngineGuard.ValidateRequest(request);
            RunnerEngineGuard.ValidateDatabaseConnectivity(_databaseClientFactory.CreateClient());

            runOutputDirectory = RunnerOutputDirectoryFactory.CreateRunOutputDirectory(request.OutputDirectory);
            var runFilesDirectory = RunnerOutputDirectoryFactory.CreateRunFilesDirectory(runOutputDirectory);
            eventEmitter = new RunnerEventEmitter(writer, request, cancellationToken);

            await eventEmitter.EmitAsync(RunnerStep.RequestAccepted, "Run request accepted.");

            var (resolvedTargets, bigScriptTargetCodes) = await _catalogService.ResolveAsync(request.TargetCodes, cancellationToken);
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
                await eventEmitter.EmitForScriptAsync(script, RunnerStep.ScriptDiscovered, $"Discovered script {script.ScriptCode}.");

            if (scripts.Length == 0)
            {
                await eventEmitter.EmitAsync(RunnerStep.Completed, "No scripts found for selected targets.");
                return;
            }

            var distributor = new ScriptDistributor(scripts, bigScriptTargetCodes);
            var memberChunkCounters = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var remainingScriptsByMember = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in scripts
                         .Where(static s => !string.IsNullOrWhiteSpace(s.MemberName))
                         .GroupBy(static s => s.MemberName.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                remainingScriptsByMember[group.Key] = group.Count();
            }

            var bigWorkerCount = Math.Max(0, _options.WorkerCount - 1);

            var workers = new List<Task>(_options.WorkerCount);
            for (var workerId = 1; workerId <= _options.WorkerCount; workerId++)
            {
                var queuePreference = workerId <= bigWorkerCount
                    ? WorkerQueuePreference.BigFirst
                    : WorkerQueuePreference.LightFirst;

                var capturedWorkerId = workerId;
                var capturedQueuePreference = queuePreference;
                workers.Add(Task.Run(
                    () => RunWorkerAsync(
                        capturedWorkerId,
                        capturedQueuePreference,
                        distributor,
                        runFilesDirectory,
                        eventEmitter,
                        senderBatchBuilder,
                        reportRows,
                        memberChunkCounters,
                        remainingScriptsByMember,
                        request.PublishToGateway,
                        request.CorrelationId,
                        cancellationToken),
                    cancellationToken));
            }

            await Task.WhenAll(workers);

            var reportPath = Path.Combine(runOutputDirectory, RunnerOutputDirectoryFactory.RunReportFileName);
            await RunReportCsvWriter.WriteAsync(reportPath, reportRows, CancellationToken.None);
            await eventEmitter.EmitAsync(
                RunnerStep.FileWritten,
                $"File written: {Path.GetFileName(reportPath)}.",
                filePath: reportPath);
            await eventEmitter.EmitAsync(
                RunnerStep.Completed,
                $"Run completed successfully. Output: {runOutputDirectory}",
                filePath: runOutputDirectory);

            if (!request.PublishToGateway)
            {
                await eventEmitter.EmitAsync(
                    RunnerStep.PublishedToGateway,
                    "Gateway publish skipped by request (PublishToGateway=false).");
                return;
            }

            foreach (var batchEvent in senderBatchBuilder.BuildBatchEvents(request.CorrelationId))
            {
                await _gatewayPublisher.PublishFileBatchReadyAsync(batchEvent, cancellationToken);
            }
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
        }
    }

    private async Task RunWorkerAsync(
        int workerId,
        WorkerQueuePreference queuePreference,
        ScriptDistributor distributor,
        string runFilesDirectory,
        RunnerEventEmitter eventEmitter,
        SenderBatchBuilder senderBatchBuilder,
        ConcurrentBag<RunReportRow> reportRows,
        ConcurrentDictionary<string, int> memberChunkCounters,
        ConcurrentDictionary<string, int> remainingScriptsByMember,
        bool publishToGateway,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var client = _databaseClientFactory.CreateClient();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!distributor.TryTakeNext(queuePreference, out var script))
                    break;

                cancellationToken.ThrowIfCancellationRequested();
                await ProcessScriptAsync(
                    script,
                    workerId,
                    client,
                    runFilesDirectory,
                    eventEmitter,
                    senderBatchBuilder,
                    reportRows,
                    memberChunkCounters,
                    remainingScriptsByMember,
                    publishToGateway,
                    correlationId,
                    cancellationToken);
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
        SenderBatchBuilder senderBatchBuilder,
        ConcurrentBag<RunReportRow> reportRows,
        ConcurrentDictionary<string, int> memberChunkCounters,
        ConcurrentDictionary<string, int> remainingScriptsByMember,
        bool publishToGateway,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await eventEmitter.EmitForScriptAsync(
            script,
            RunnerStep.QueryStarted,
            $"Worker #{workerId} running query for script {script.ScriptCode}.",
            workerId: workerId);

        await using var reader = await client.GetDataReaderAsync(script.SqlText, cancellationToken);
        var columns = RunnerEngineDataReader.GetColumns(reader);

        if (columns.Count == 0)
            throw new InvalidOperationException($"Query for script '{script.ScriptCode}' returned no columns.");

        var dayOfYear = DateTimeOffset.Now.DayOfYear;
        var headerLine =
            $"#|{script.ScriptType}|{script.OutputFileStem}{dayOfYear:D3}{int.MaxValue}{script.OutputFileExtension}|{OutputFormatConstants.SenderCode}|{DateTimeOffset.Now:yyyy-MM-dd}|{int.MaxValue}|{script.FirstCodeDigit}";
        var headerSize = PipeDelimitedFormatter.EstimateLineBytes(headerLine);

        var rowsRead = 0;
        var currentRows = new List<DatabaseRow>();
        var currentSize = headerSize;

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = RunnerEngineDataReader.ReadRow(reader, columns);
            var line = PipeDelimitedFormatter.BuildDataLine(row, columns);
            var rowSize = PipeDelimitedFormatter.EstimateLineBytes(line);
            if (rowSize + headerSize > _options.ChunkSizeBytes)
                throw new InvalidOperationException($"Single row exceeds chunk size {_options.ChunkSizeBytes} bytes.");
            if (currentRows.Count > 0 && currentSize + rowSize > _options.ChunkSizeBytes)
            {
                var chunkNumber = memberChunkCounters.AddOrUpdate(script.MemberName, 1, static (_, c) => checked(c + 1));
                await WriteAndPublishChunkAsync(
                    script,
                    workerId,
                    chunkNumber,
                    currentRows.ToArray(),
                    currentSize,
                    runFilesDirectory,
                    eventEmitter,
                    senderBatchBuilder,
                    reportRows,
                    cancellationToken);
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
                script,
                workerId,
                chunkNumber,
                currentRows.ToArray(),
                currentSize,
                runFilesDirectory,
                eventEmitter,
                senderBatchBuilder,
                reportRows,
                cancellationToken);
        }
        else if (rowsRead == 0)
            reportRows.Add(new RunReportRow(script.MemberName, script.ScriptType, script.FirstCodeDigit, string.Empty, 0, false, 0));

        await eventEmitter.EmitForScriptAsync(
            script,
            RunnerStep.QueryCompleted,
            $"Worker #{workerId} finished query for script {script.ScriptCode}.",
            records: rowsRead,
            workerId: workerId);

        if (!string.IsNullOrWhiteSpace(script.MemberName))
        {
            var memberName = script.MemberName.Trim();
            var remaining = remainingScriptsByMember.AddOrUpdate(
                memberName,
                0,
                static (_, current) => current <= 0 ? 0 : checked(current - 1));

            if (remaining == 0 &&
                senderBatchBuilder.TryBuildMemberBatchEvent(correlationId, memberName, out var memberBatch))
            {
                if (publishToGateway)
                {
                    await _gatewayPublisher.PublishFileBatchReadyAsync(memberBatch, cancellationToken);
                }
            }
            else if (remaining == 0)
            {
                await eventEmitter.EmitForScriptAsync(
                    script,
                    RunnerStep.ScriptCompleted,
                    "Member completed. No output files were produced.",
                    records: rowsRead,
                    filePath: null,
                    workerId: workerId,
                    cancellationToken: cancellationToken);
            }
        }
    }

    private async Task WriteAndPublishChunkAsync(
        ScriptDefinition script,
        int workerId,
        int chunkNumber,
        DatabaseRow[] rows,
        int byteSize,
        string runFilesDirectory,
        RunnerEventEmitter eventEmitter,
        SenderBatchBuilder senderBatchBuilder,
        ConcurrentBag<RunReportRow> reportRows,
        CancellationToken cancellationToken)
    {
        await eventEmitter.EmitForScriptAsync(
            script,
            RunnerStep.ChunkCreated,
            $"Chunk #{chunkNumber} created for {script.ScriptCode}.",
            records: rows.Length,
            workerId: workerId);

        var chunk = new FileChunk(script, chunkNumber, rows, byteSize);
        var stopwatch = Stopwatch.StartNew();
        var written = await _fileChunkWriter.WriteChunkAsync(chunk, runFilesDirectory, cancellationToken);
        stopwatch.Stop();
        senderBatchBuilder.Add(script.MemberName, written.FilePath);

        await eventEmitter.EmitForScriptAsync(
            script,
            RunnerStep.FileWritten,
            $"File written: {Path.GetFileName(written.FilePath)}.",
            records: written.RowsCount,
            filePath: written.FilePath,
            workerId: workerId,
            cancellationToken: cancellationToken);

        reportRows.Add(new RunReportRow(
            script.MemberName,
            script.ScriptType,
            script.FirstCodeDigit,
            Path.GetFileName(written.FilePath),
            written.RowsCount,
            true,
            stopwatch.ElapsedMilliseconds));
    }
}
