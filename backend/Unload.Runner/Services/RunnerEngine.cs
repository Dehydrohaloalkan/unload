using System.Data.Common;
using System.Diagnostics;
using System.Threading.Channels;
using Unload.Core;

namespace Unload.Runner;

public class RunnerEngine : IRunner
{
    private readonly ICatalogService _catalogService;
    private readonly IDatabaseClient _databaseClient;
    private readonly IFileChunkWriter _fileChunkWriter;
    private readonly IMqPublisher _mqPublisher;
    private readonly IRunDiagnosticsSink _diagnosticsSink;
    private readonly IRequestHasher _requestHasher;
    private readonly RunnerOptions _options;

    public RunnerEngine(
        ICatalogService catalogService,
        IDatabaseClient databaseClient,
        IFileChunkWriter fileChunkWriter,
        IMqPublisher mqPublisher,
        IRunDiagnosticsSink diagnosticsSink,
        IRequestHasher requestHasher,
        RunnerOptions options)
    {
        _catalogService = catalogService;
        _databaseClient = databaseClient;
        _fileChunkWriter = fileChunkWriter;
        _mqPublisher = mqPublisher;
        _diagnosticsSink = diagnosticsSink;
        _requestHasher = requestHasher;
        _options = options;
    }

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

    private async Task ExecutePipelineAsync(
        RunRequest request,
        ChannelWriter<RunnerEvent> writer,
        CancellationToken cancellationToken)
    {
        var runStopwatch = Stopwatch.StartNew();
        try
        {
            ValidateRequest(request);
            ValidateDatabaseConnectivity();

            var runHash = _requestHasher.ComputeHash($"{request.CorrelationId}:{DateTimeOffset.UtcNow:O}")[..12];
            var runOutputDirectory = Path.Combine(request.OutputDirectory, runHash);
            Directory.CreateDirectory(runOutputDirectory);

            await EmitAsync(
                writer,
                request,
                RunnerStep.RequestAccepted,
                "Run request accepted.",
                cancellationToken: cancellationToken);

            var resolveStopwatch = Stopwatch.StartNew();
            var resolvedProfiles = await _catalogService.ResolveAsync(request.ProfileCodes, cancellationToken);
            resolveStopwatch.Stop();
            await EmitAsync(
                writer,
                request,
                RunnerStep.ProfilesResolved,
                $"Profiles resolved: {resolvedProfiles.Count}.",
                records: resolvedProfiles.Count,
                cancellationToken: cancellationToken);
            await _diagnosticsSink.WriteMetricAsync(
                new RunMetricRecord(
                    DateTimeOffset.UtcNow,
                    request.CorrelationId,
                    RunnerStep.ProfilesResolved,
                    resolveStopwatch.ElapsedMilliseconds,
                    "success",
                    Records: resolvedProfiles.Count,
                    Details: "Catalog profiles resolved."),
                cancellationToken);

            var scripts = resolvedProfiles
                .SelectMany(static x => x.Value)
                .OrderBy(static x => x.ProfileCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static x => x.ScriptCode, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var script in scripts)
            {
                await EmitAsync(
                    writer,
                    request,
                    RunnerStep.ScriptDiscovered,
                    $"Discovered script {script.ScriptCode}.",
                    profileCode: script.ProfileCode,
                    scriptCode: script.ScriptCode,
                    cancellationToken: cancellationToken);
            }

            if (scripts.Length == 0)
            {
                await EmitAsync(
                    writer,
                    request,
                    RunnerStep.Completed,
                    "No scripts found for selected profiles.",
                    cancellationToken: cancellationToken);
                return;
            }

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism
            };

            await Parallel.ForEachAsync(scripts, parallelOptions, async (script, token) =>
            {
                await ProcessScriptAsync(request, script, runOutputDirectory, writer, token);
            });

            runStopwatch.Stop();
            await EmitAsync(
                writer,
                request,
                RunnerStep.Completed,
                $"Run completed successfully. Output: {runOutputDirectory}",
                filePath: runOutputDirectory,
                cancellationToken: cancellationToken);
            await _diagnosticsSink.WriteMetricAsync(
                new RunMetricRecord(
                    DateTimeOffset.UtcNow,
                    request.CorrelationId,
                    RunnerStep.Completed,
                    runStopwatch.ElapsedMilliseconds,
                    "success",
                    FilePath: runOutputDirectory,
                    Details: "Run completed."),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            runStopwatch.Stop();
            await EmitAsync(
                writer,
                request,
                RunnerStep.Failed,
                "Run was cancelled.",
                cancellationToken: CancellationToken.None);
            await _diagnosticsSink.WriteMetricAsync(
                new RunMetricRecord(
                    DateTimeOffset.UtcNow,
                    request.CorrelationId,
                    RunnerStep.Failed,
                    runStopwatch.ElapsedMilliseconds,
                    "cancelled",
                    Details: "Run cancelled by cancellation token."),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            runStopwatch.Stop();
            await EmitAsync(
                writer,
                request,
                RunnerStep.Failed,
                ex.Message,
                cancellationToken: CancellationToken.None);
            await _diagnosticsSink.WriteMetricAsync(
                new RunMetricRecord(
                    DateTimeOffset.UtcNow,
                    request.CorrelationId,
                    RunnerStep.Failed,
                    runStopwatch.ElapsedMilliseconds,
                    "error",
                    Details: ex.Message),
                CancellationToken.None);
        }
    }

    private async Task ProcessScriptAsync(
        RunRequest request,
        ScriptDefinition script,
        string runOutputDirectory,
        ChannelWriter<RunnerEvent> writer,
        CancellationToken cancellationToken)
    {
        var scriptStopwatch = Stopwatch.StartNew();
        await EmitAsync(
            writer,
            request,
            RunnerStep.QueryStarted,
            $"Running query for script {script.ScriptCode}.",
            profileCode: script.ProfileCode,
            scriptCode: script.ScriptCode,
            cancellationToken: cancellationToken);

        var queryStopwatch = Stopwatch.StartNew();
        await using var reader = await _databaseClient.GetDataReaderAsync(script.SqlText, cancellationToken);
        var columns = GetColumns(reader);

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"Query for script '{script.ScriptCode}' returned no columns.");
        }

        var rowsRead = 0;
        var chunkNumber = 1;
        var currentRows = new List<DatabaseRow>();
        var headerLine = PipeDelimitedFormatter.BuildHeaderLine(columns);
        var headerSize = PipeDelimitedFormatter.EstimateLineBytes(headerLine);
        var currentSize = headerSize;

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = ReadRow(reader, columns);
            var line = PipeDelimitedFormatter.BuildDataLine(row, columns);
            var rowSize = PipeDelimitedFormatter.EstimateLineBytes(line);
            if (rowSize + headerSize > _options.ChunkSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Single row size {rowSize} bytes exceeds chunk size {_options.ChunkSizeBytes} bytes.");
            }

            if (currentRows.Count > 0 && currentSize + rowSize > _options.ChunkSizeBytes)
            {
                await FlushChunkAsync(
                    request,
                    script,
                    runOutputDirectory,
                    writer,
                    chunkNumber,
                    currentRows,
                    currentSize,
                    cancellationToken);
                chunkNumber++;
                currentRows = [];
                currentSize = headerSize;
            }

            currentRows.Add(row);
            currentSize += rowSize;
            rowsRead++;
        }

        queryStopwatch.Stop();
        await EmitAsync(
            writer,
            request,
            RunnerStep.QueryCompleted,
            $"Query finished for script {script.ScriptCode}.",
            profileCode: script.ProfileCode,
            scriptCode: script.ScriptCode,
            records: rowsRead,
            cancellationToken: cancellationToken);
        await _diagnosticsSink.WriteMetricAsync(
            new RunMetricRecord(
                DateTimeOffset.UtcNow,
                request.CorrelationId,
                RunnerStep.QueryCompleted,
                queryStopwatch.ElapsedMilliseconds,
                "success",
                script.ProfileCode,
                script.ScriptCode,
                rowsRead,
                Details: "Database query execution completed."),
            cancellationToken);

        if (currentRows.Count > 0)
        {
            await FlushChunkAsync(
                request,
                script,
                runOutputDirectory,
                writer,
                chunkNumber,
                currentRows,
                currentSize,
                cancellationToken);
        }

        scriptStopwatch.Stop();
        await _diagnosticsSink.WriteMetricAsync(
            new RunMetricRecord(
                DateTimeOffset.UtcNow,
                request.CorrelationId,
                RunnerStep.ScriptCompleted,
                scriptStopwatch.ElapsedMilliseconds,
                "success",
                script.ProfileCode,
                script.ScriptCode,
                rowsRead,
                Details: "Script export completed (query + chunk writing)."),
            cancellationToken);
    }

    private async Task FlushChunkAsync(
        RunRequest request,
        ScriptDefinition script,
        string runOutputDirectory,
        ChannelWriter<RunnerEvent> writer,
        int chunkNumber,
        IReadOnlyList<DatabaseRow> rows,
        int byteSize,
        CancellationToken cancellationToken)
    {
        var chunk = new FileChunk(script, chunkNumber, rows.ToArray(), byteSize);
        await EmitAsync(
            writer,
            request,
            RunnerStep.ChunkCreated,
            $"Chunk #{chunk.ChunkNumber} created for {script.ScriptCode}.",
            profileCode: script.ProfileCode,
            scriptCode: script.ScriptCode,
            records: chunk.Rows.Count,
            cancellationToken: cancellationToken);

        var writeStopwatch = Stopwatch.StartNew();
        var written = await _fileChunkWriter.WriteChunkAsync(chunk, runOutputDirectory, cancellationToken);
        writeStopwatch.Stop();
        await EmitAsync(
            writer,
            request,
            RunnerStep.FileWritten,
            $"File written: {Path.GetFileName(written.FilePath)}.",
            profileCode: script.ProfileCode,
            scriptCode: script.ScriptCode,
            records: written.RowsCount,
            filePath: written.FilePath,
            cancellationToken: cancellationToken);
        await _diagnosticsSink.WriteMetricAsync(
            new RunMetricRecord(
                DateTimeOffset.UtcNow,
                request.CorrelationId,
                RunnerStep.FileWritten,
                writeStopwatch.ElapsedMilliseconds,
                "success",
                script.ProfileCode,
                script.ScriptCode,
                written.RowsCount,
                written.FilePath,
                "Chunk file written."),
            cancellationToken);
    }

    private async Task EmitAsync(
        ChannelWriter<RunnerEvent> writer,
        RunRequest request,
        RunnerStep step,
        string message,
        string? profileCode = null,
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
            profileCode,
            scriptCode,
            records,
            filePath);

        await _diagnosticsSink.WriteEventAsync(@event, cancellationToken);
        await _mqPublisher.PublishAsync(@event, cancellationToken);
        await writer.WriteAsync(@event, cancellationToken);
    }

    private static List<string> GetColumns(DbDataReader reader)
    {
        var columns = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        return columns;
    }

    private static DatabaseRow ReadRow(DbDataReader reader, IReadOnlyList<string> columns)
    {
        var values = new Dictionary<string, object?>(columns.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
        {
            values[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return new DatabaseRow(values);
    }

    private void ValidateDatabaseConnectivity()
    {
        if (!_databaseClient.IsConnected)
        {
            throw new InvalidOperationException("Database connection is not available.");
        }
    }

    private static void ValidateRequest(RunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            throw new InvalidOperationException("CorrelationId is required.");
        }

        if (request.ProfileCodes.Count == 0)
        {
            throw new InvalidOperationException("At least one profile code is required.");
        }
    }
}
