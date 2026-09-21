using System.Threading.Channels;
using Unload.Core;

namespace Unload.Tasks.MainUnload;

internal class RunnerEventEmitter
{
    private const int EventChannelCapacity = 64;
    private readonly Channel<RunnerEvent> _channel;
    private readonly Task _consumerTask;
    private readonly string _correlationId;

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
        int? batchFileCount = null)
    {
        return EmitCoreAsync(step, message, null, records, filePath, workerId: null, chunkNumber, estimatedBytes, batchId, batchFileCount, CancellationToken.None).AsTask();
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
        int? batchFileCount = null)
    {
        return EmitCoreAsync(step, message, null, records, filePath, workerId: null, chunkNumber, estimatedBytes, batchId, batchFileCount, cancellationToken).AsTask();
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
        int? batchFileCount = null)
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
        int? batchFileCount = null)
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
            cancellationToken);
    }

    public async Task TryEmitFailureAsync(RunnerStep step, string message)
    {
        try
        {
            await EmitAsync(step, message, records: null, filePath: null, chunkNumber: null, estimatedBytes: null, batchId: null, batchFileCount: null, cancellationToken: CancellationToken.None);
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
        CancellationToken cancellationToken)
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
            batchFileCount);
        await _channel.Writer.WriteAsync(@event, cancellationToken);
    }
}
