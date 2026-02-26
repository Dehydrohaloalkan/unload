using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using Unload.Core;

namespace Unload.Runner;

public class RunnerEngine : IRunner
{
    private readonly ICatalogService _catalogService;
    private readonly IDatabaseClient _databaseClient;
    private readonly IFileChunkWriter _fileChunkWriter;
    private readonly IMqPublisher _mqPublisher;
    private readonly IRequestHasher _requestHasher;
    private readonly RunnerOptions _options;

    public RunnerEngine(
        ICatalogService catalogService,
        IDatabaseClient databaseClient,
        IFileChunkWriter fileChunkWriter,
        IMqPublisher mqPublisher,
        IRequestHasher requestHasher,
        RunnerOptions options)
    {
        _catalogService = catalogService;
        _databaseClient = databaseClient;
        _fileChunkWriter = fileChunkWriter;
        _mqPublisher = mqPublisher;
        _requestHasher = requestHasher;
        _options = options;
    }

    public IAsyncEnumerable<RunnerEvent> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<RunnerEvent>();

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
        }, cancellationToken);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task ExecutePipelineAsync(
        RunRequest request,
        ChannelWriter<RunnerEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateRequest(request);

            var runHash = _requestHasher.ComputeHash($"{request.CorrelationId}:{DateTimeOffset.UtcNow:O}")[..12];
            var runOutputDirectory = Path.Combine(request.OutputDirectory, runHash);
            Directory.CreateDirectory(runOutputDirectory);

            await EmitAsync(writer, request, RunnerStep.RequestAccepted, "Run request accepted.");

            var resolvedProfiles = await _catalogService.ResolveAsync(request.ProfileCodes, cancellationToken);
            await EmitAsync(
                writer,
                request,
                RunnerStep.ProfilesResolved,
                $"Profiles resolved: {resolvedProfiles.Count}.",
                records: resolvedProfiles.Count);

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
                    scriptCode: script.ScriptCode);
            }

            if (scripts.Length == 0)
            {
                await EmitAsync(writer, request, RunnerStep.Completed, "No scripts found for selected profiles.");
                return;
            }

            var blockOptions = new ExecutionDataflowBlockOptions
            {
                CancellationToken = cancellationToken,
                BoundedCapacity = _options.DataflowBoundedCapacity,
                MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism
            };

            var queryBlock = new TransformBlock<ScriptDefinition, ScriptRows>(async script =>
            {
                await EmitAsync(
                    writer,
                    request,
                    RunnerStep.QueryStarted,
                    $"Running query for script {script.ScriptCode}.",
                    profileCode: script.ProfileCode,
                    scriptCode: script.ScriptCode);

                var rows = new List<DatabaseRow>();
                await foreach (var row in _databaseClient.ExecuteScriptAsync(script, cancellationToken))
                {
                    rows.Add(row);
                }

                await EmitAsync(
                    writer,
                    request,
                    RunnerStep.QueryCompleted,
                    $"Query finished for script {script.ScriptCode}.",
                    profileCode: script.ProfileCode,
                    scriptCode: script.ScriptCode,
                    records: rows.Count);

                return new ScriptRows(script, rows);
            }, blockOptions);

            var chunkBlock = new TransformBlock<ScriptRows, IReadOnlyList<FileChunk>>(async batch =>
            {
                var chunks = CreateChunks(batch.Script, batch.Rows, _options.ChunkSizeBytes);
                foreach (var chunk in chunks)
                {
                    await EmitAsync(
                        writer,
                        request,
                        RunnerStep.ChunkCreated,
                        $"Chunk #{chunk.ChunkNumber} created for {batch.Script.ScriptCode}.",
                        profileCode: batch.Script.ProfileCode,
                        scriptCode: batch.Script.ScriptCode,
                        records: chunk.Rows.Count);
                }

                return chunks;
            }, blockOptions);

            var flattenBlock = new TransformManyBlock<IReadOnlyList<FileChunk>, FileChunk>(chunks => chunks, blockOptions);

            var writeBlock = new ActionBlock<FileChunk>(async chunk =>
            {
                var written = await _fileChunkWriter.WriteChunkAsync(chunk, runOutputDirectory, cancellationToken);
                await EmitAsync(
                    writer,
                    request,
                    RunnerStep.FileWritten,
                    $"File written: {Path.GetFileName(written.FilePath)}.",
                    profileCode: written.Script.ProfileCode,
                    scriptCode: written.Script.ScriptCode,
                    records: written.RowsCount,
                    filePath: written.FilePath);
            }, blockOptions);

            queryBlock.LinkTo(chunkBlock, new DataflowLinkOptions { PropagateCompletion = true });
            chunkBlock.LinkTo(flattenBlock, new DataflowLinkOptions { PropagateCompletion = true });
            flattenBlock.LinkTo(writeBlock, new DataflowLinkOptions { PropagateCompletion = true });

            foreach (var script in scripts)
            {
                await queryBlock.SendAsync(script, cancellationToken);
            }

            queryBlock.Complete();
            await writeBlock.Completion;

            await EmitAsync(
                writer,
                request,
                RunnerStep.Completed,
                $"Run completed successfully. Output: {runOutputDirectory}",
                filePath: runOutputDirectory);
        }
        catch (Exception ex)
        {
            await EmitAsync(writer, request, RunnerStep.Failed, ex.Message);
        }
    }

    private async Task EmitAsync(
        ChannelWriter<RunnerEvent> writer,
        RunRequest request,
        RunnerStep step,
        string message,
        string? profileCode = null,
        string? scriptCode = null,
        int? records = null,
        string? filePath = null)
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

        await _mqPublisher.PublishAsync(@event, CancellationToken.None);
        await writer.WriteAsync(@event, CancellationToken.None);
    }

    private static IReadOnlyList<FileChunk> CreateChunks(
        ScriptDefinition script,
        IReadOnlyList<DatabaseRow> rows,
        int chunkSizeBytes)
    {
        if (chunkSizeBytes <= 0)
        {
            throw new InvalidOperationException("Chunk size must be greater than zero.");
        }

        var chunks = new List<FileChunk>();
        var currentRows = new List<DatabaseRow>();
        var columns = PipeDelimitedFormatter.GetOrderedColumns(rows);
        var headerLine = PipeDelimitedFormatter.BuildHeaderLine(columns);
        var headerSize = PipeDelimitedFormatter.EstimateLineBytes(headerLine);
        var currentSize = headerSize;
        var chunkNumber = 1;

        foreach (var row in rows)
        {
            var line = PipeDelimitedFormatter.BuildDataLine(row, columns);
            var rowSize = PipeDelimitedFormatter.EstimateLineBytes(line);

            if (rowSize + headerSize > chunkSizeBytes)
            {
                throw new InvalidOperationException(
                    $"Single row size {rowSize} bytes exceeds chunk size {chunkSizeBytes} bytes.");
            }

            if (currentRows.Count > 0 && currentSize + rowSize > chunkSizeBytes)
            {
                chunks.Add(new FileChunk(script, chunkNumber, currentRows.ToArray(), currentSize));
                chunkNumber++;
                currentRows = [];
                currentSize = headerSize;
            }

            currentRows.Add(row);
            currentSize += rowSize;
        }

        if (currentRows.Count > 0)
        {
            chunks.Add(new FileChunk(script, chunkNumber, currentRows.ToArray(), currentSize));
        }

        return chunks;
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

    private record ScriptRows(
        ScriptDefinition Script,
        IReadOnlyList<DatabaseRow> Rows);
}
