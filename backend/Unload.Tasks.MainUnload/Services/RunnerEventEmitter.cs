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
        string? filePath = null,
        string? batchId = null,
        RunnerFailureInfo? failure = null,
        IReadOnlyCollection<SenderBatchFileStatusInfo>? batchFiles = null,
        CancellationToken cancellationToken = default) =>
        EmitCoreAsync(step, message, null, filePath, batchId, failure, batchFiles, cancellationToken).AsTask();

    public Task EmitForScriptAsync(
        ScriptDefinition script,
        RunnerStep step,
        string message,
        string? filePath = null,
        string? batchId = null,
        RunnerFailureInfo? failure = null,
        IReadOnlyCollection<SenderBatchFileStatusInfo>? batchFiles = null,
        CancellationToken cancellationToken = default) =>
        EmitCoreAsync(step, message, script, filePath, batchId, failure, batchFiles, cancellationToken).AsTask();

    public async Task TryEmitFailureAsync(
        RunnerStep step,
        string message,
        RunnerFailureInfo? failure = null,
        ScriptDefinition? script = null,
        string? batchId = null)
    {
        try
        {
            if (script is null)
            {
                await EmitAsync(step, message, batchId: batchId, failure: failure);
            }
            else
            {
                await EmitForScriptAsync(
                    script,
                    step,
                    message,
                    filePath: failure?.FilePath,
                    batchId: batchId,
                    failure: failure);
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
        string? filePath,
        string? batchId,
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
                filePath,
                batchId,
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
