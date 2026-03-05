using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using Unload.Core;

namespace Unload.Runner;

/// <summary>
/// Helper для компактной публикации событий раннера через общий dataflow-блок.
/// </summary>
internal sealed class RunnerEventEmitter
{
    private readonly ActionBlock<PendingRunnerEvent> _eventBlock;
    private readonly RunRequest _request;
    private readonly CancellationToken _defaultCancellationToken;

    public RunnerEventEmitter(
        IMqPublisher mqPublisher,
        RunnerOptions options,
        ChannelWriter<RunnerEvent> writer,
        RunRequest request,
        CancellationToken cancellationToken)
    {
        _request = request;
        _defaultCancellationToken = cancellationToken;
        _eventBlock = new ActionBlock<PendingRunnerEvent>(async pending =>
        {
            try
            {
                var isSentToMq = await TryPublishToMqAsync(mqPublisher, pending.Event, cancellationToken);
                await writer.WriteAsync(pending.Event, cancellationToken);
                pending.Completion?.TrySetResult(isSentToMq);
            }
            catch (Exception ex)
            {
                pending.Completion?.TrySetException(ex);
                throw;
            }
        }, new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = options.QueuePublisherDegreeOfParallelism,
            BoundedCapacity = options.DataflowBoundedCapacity,
            CancellationToken = cancellationToken
        });
    }

    public Task EmitAsync(
        RunnerStep step,
        string message,
        int? records = null,
        string? filePath = null)
    {
        return EmitCoreAsync(
            step: step,
            message: message,
            script: null,
            records: records,
            filePath: filePath,
            awaitMqStatus: false,
            cancellationToken: _defaultCancellationToken).AsTask();
    }

    public Task EmitAsync(
        RunnerStep step,
        string message,
        int? records,
        string? filePath,
        CancellationToken cancellationToken)
    {
        return EmitCoreAsync(
            step: step,
            message: message,
            script: null,
            records: records,
            filePath: filePath,
            awaitMqStatus: false,
            cancellationToken: cancellationToken).AsTask();
    }

    public async Task<bool> EmitForScriptAsync(
        ScriptDefinition script,
        RunnerStep step,
        string message,
        int? records = null,
        string? filePath = null,
        bool awaitMqStatus = false)
    {
        return await EmitCoreAsync(
            step,
            message,
            script,
            records,
            filePath,
            awaitMqStatus,
            _defaultCancellationToken) ?? false;
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
        return await EmitCoreAsync(
            step,
            message,
            script,
            records,
            filePath,
            awaitMqStatus,
            cancellationToken) ?? false;
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
        _eventBlock.Complete();
        try
        {
            await _eventBlock.Completion;
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
            _request.CorrelationId,
            step,
            message,
            script?.TargetCode,
            script?.MemberName,
            script?.ScriptCode,
            records,
            filePath);
        var accepted = await _eventBlock.SendAsync(new PendingRunnerEvent(@event, completion), cancellationToken);
        if (!accepted)
        {
            throw new InvalidOperationException("Event stage declined event message.");
        }

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
