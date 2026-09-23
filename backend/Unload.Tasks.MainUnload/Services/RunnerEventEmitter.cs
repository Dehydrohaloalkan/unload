using System.Threading.Channels;
using Unload.Core;

namespace Unload.Tasks.MainUnload;

internal class RunnerEventEmitter
{
    private const int EventChannelCapacity = 64;
    private readonly Channel<RunnerEvent> _channel;
    private readonly Task _consumerTask;
    private readonly string _correlationId;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly RunnerEventSequencer _sequencer = new();

    public RunnerEventEmitter(
        ChannelWriter<RunnerEvent> writer,
        RunRequest request,
        CancellationToken cancellationToken)
    {
        _correlationId = request.CorrelationId;
        _channel = Channel.CreateBounded<RunnerEvent>(new BoundedChannelOptions(EventChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var @event in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    await writer.WriteAsync(@event, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    public Task EmitAsync(
        RunnerStep step,
        string message,
        int? records = null,
        string? filePath = null,
        int? chunkNumber = null,
        long? estimatedBytes = null,
        string? batchId = null,
        int? batchFileCount = null,
        RunnerFailureInfo? failure = null,
        IReadOnlyCollection<SenderBatchFileStatusInfo>? batchFiles = null)
    {
        return EmitCoreAsync(step, message, null, records, filePath, workerId: null, chunkNumber, estimatedBytes, batchId, batchFileCount, failure, batchFiles, CancellationToken.None).AsTask();
    }

    public Task EmitAsync(
        RunnerStep step,
        string message,
        int? records,
        string? filePath,
        CancellationToken cancellationToken,
        int? chunkNumber = null,
        long? estimatedBytes = null,
        string? batchId = null,
        int? batchFileCount = null,
        RunnerFailureInfo? failure = null,
        IReadOnlyCollection<SenderBatchFileStatusInfo>? batchFiles = null)
    {
        return EmitCoreAsync(step, message, null, records, filePath, workerId: null, chunkNumber, estimatedBytes, batchId, batchFileCount, failure, batchFiles, cancellationToken).AsTask();
    }

    public async Task EmitForScriptAsync(
        ScriptDefinition script,
        RunnerStep step,
        string message,
        int? records = null,
        string? filePath = null,
        int? workerId = null,
        int? chunkNumber = null,
        long? estimatedBytes = null,
        string? batchId = null,
        int? batchFileCount = null,
        RunnerFailureInfo? failure = null,
        IReadOnlyCollection<SenderBatchFileStatusInfo>? batchFiles = null)
    {
        await EmitCoreAsync(
            step,
            message,
            script,
            records,
            filePath,
            workerId,
            chunkNumber,
            estimatedBytes,
            batchId,
            batchFileCount,
            failure,
            batchFiles,
            CancellationToken.None);
    }

    public async Task EmitForScriptAsync(
        ScriptDefinition script,
        RunnerStep step,
        string message,
        int? records,
        string? filePath,
        int? workerId,
        CancellationToken cancellationToken,
        int? chunkNumber = null,
        long? estimatedBytes = null,
        string? batchId = null,
        int? batchFileCount = null,
        RunnerFailureInfo? failure = null,
        IReadOnlyCollection<SenderBatchFileStatusInfo>? batchFiles = null)
    {
        await EmitCoreAsync(
            step,
            message,
            script,
            records,
            filePath,
            workerId,
            chunkNumber,
            estimatedBytes,
            batchId,
            batchFileCount,
            failure,
            batchFiles,
            cancellationToken);
    }

    public async Task TryEmitFailureAsync(
        RunnerStep step,
        string message,
        RunnerFailureInfo? failure = null,
        ScriptDefinition? script = null,
        int? workerId = null,
        int? chunkNumber = null,
        string? batchId = null)
    {
        try
        {
            if (script is null)
            {
                await EmitAsync(step, message, records: null, filePath: null, chunkNumber: chunkNumber, estimatedBytes: null, batchId: batchId, batchFileCount: null, failure: failure, batchFiles: null, cancellationToken: CancellationToken.None);
            }
            else
            {
                await EmitForScriptAsync(
                    script,
                    step,
                    message,
                    records: null,
                    filePath: failure?.FilePath,
                    workerId: workerId,
                    chunkNumber: chunkNumber,
                    estimatedBytes: null,
                    batchId: batchId,
                    batchFileCount: null,
                    failure: failure,
                    batchFiles: null,
                    cancellationToken: CancellationToken.None);
            }
        }
        catch
        {
        }
    }

    public async Task CompleteAsync()
    {
        _channel.Writer.Complete();
        try
        {
            await _consumerTask;
        }
        catch
        {
        }
    }

    private async ValueTask EmitCoreAsync(
        RunnerStep step,
        string message,
        ScriptDefinition? script,
        int? records,
        string? filePath,
        int? workerId,
        int? chunkNumber,
        long? estimatedBytes,
        string? batchId,
        int? batchFileCount,
        RunnerFailureInfo? failure,
        IReadOnlyCollection<SenderBatchFileStatusInfo>? batchFiles,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var @event = new RunnerEvent(
                DateTimeOffset.UtcNow,
                _correlationId,
                step,
                message,
                script?.MemberName,
                script?.ScriptCode,
                records,
                filePath,
                workerId,
                chunkNumber,
                estimatedBytes,
                batchId,
                batchFileCount,
                Sequence: _sequencer.Next(),
                WorkOrder: script?.WorkOrder,
                Failure: failure,
                BatchFiles: batchFiles);
            await _channel.Writer.WriteAsync(RunnerFailureMessages.Sanitize(@event), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
