using System.Threading.Channels;
using Unload.Core;

namespace Unload.Runner;

internal sealed class RunnerEventEmitter
{
    private const int EventChannelCapacity = 64;
    private readonly Channel<PendingRunnerEvent> _channel;
    private readonly Task _consumerTask;
    private readonly string _correlationId;

    public RunnerEventEmitter(
        IMqPublisher mqPublisher,
        ChannelWriter<RunnerEvent> writer,
        RunRequest request,
        CancellationToken cancellationToken)
    {
        _correlationId = request.CorrelationId;
        _channel = Channel.CreateBounded<PendingRunnerEvent>(new BoundedChannelOptions(EventChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var pending in _channel.Reader.ReadAllAsync(cancellationToken))
                {
                    var isSentToMq = await TryPublishToMqAsync(mqPublisher, pending.Event, cancellationToken);
                    await writer.WriteAsync(pending.Event, cancellationToken);
                    pending.Completion?.TrySetResult(isSentToMq);
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
        string? filePath = null)
    {
        return EmitCoreAsync(step, message, null, records, filePath, false, CancellationToken.None).AsTask();
    }

    public Task EmitAsync(
        RunnerStep step,
        string message,
        int? records,
        string? filePath,
        CancellationToken cancellationToken)
    {
        return EmitCoreAsync(step, message, null, records, filePath, false, cancellationToken).AsTask();
    }

    public async Task<bool> EmitForScriptAsync(
        ScriptDefinition script,
        RunnerStep step,
        string message,
        int? records = null,
        string? filePath = null,
        bool awaitMqStatus = false)
    {
        return await EmitCoreAsync(step, message, script, records, filePath, awaitMqStatus, CancellationToken.None) ?? false;
    }

    public async Task<bool> EmitForScriptAsync(
        ScriptDefinition script,
        RunnerStep step,
        string message,
        int? records,
        string? filePath,
        bool awaitMqStatus,
        CancellationToken cancellationToken)
    {
        return await EmitCoreAsync(step, message, script, records, filePath, awaitMqStatus, cancellationToken) ?? false;
    }

    public async Task TryEmitFailureAsync(RunnerStep step, string message)
    {
        try
        {
            await EmitAsync(step, message, records: null, filePath: null, cancellationToken: CancellationToken.None);
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

    private async ValueTask<bool?> EmitCoreAsync(
        RunnerStep step,
        string message,
        ScriptDefinition? script,
        int? records,
        string? filePath,
        bool awaitMqStatus,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<bool>? completion = awaitMqStatus
            ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        var @event = new RunnerEvent(
            DateTimeOffset.UtcNow,
            _correlationId,
            step,
            message,
            script?.TargetCode,
            script?.MemberName,
            script?.ScriptCode,
            records,
            filePath);
        await _channel.Writer.WriteAsync(new PendingRunnerEvent(@event, completion), cancellationToken);
        return completion is null ? null : await completion.Task;
    }

    private static async Task<bool> TryPublishToMqAsync(
        IMqPublisher mqPublisher,
        RunnerEvent @event,
        CancellationToken cancellationToken)
    {
        try
        {
            await mqPublisher.PublishAsync(@event, cancellationToken);
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

    private sealed record PendingRunnerEvent(
        RunnerEvent Event,
        TaskCompletionSource<bool>? Completion);
}
