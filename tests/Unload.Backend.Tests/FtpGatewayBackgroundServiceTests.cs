using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Unload.Core;
using Unload.Gateway;

namespace Unload.Backend.Tests;

public class FtpGatewayBackgroundServiceTests
{
    [Fact]
    public async Task UnreachableFtp_PublishesBatchStartedBeforeBatchFailed()
    {
        var batch = new SenderFileBatchReadyEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            "Member A",
            "batch-1",
            Version: 1,
            Files: []);
        var publisher = new CapturingGatewayPublisher();
        var service = new FtpGatewayBackgroundService(
            new SingleBatchSource(batch),
            publisher,
            Options.Create(new GatewayOptions
            {
                Ftp = new FtpGatewayOptions
                {
                    Host = "127.0.0.1",
                    Port = 1,
                    ConnectTimeoutMs = 1_000
                }
            }),
            NullLogger<FtpGatewayBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await publisher.TerminalFeedback.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(
            [SenderFeedbackKind.BatchStarted, SenderFeedbackKind.BatchFailed],
            publisher.Feedback.Select(static item => item.Kind));
    }

    [Fact]
    public async Task BlankMember_IsIgnoredWithoutBatchStartedFeedback()
    {
        var batch = new SenderFileBatchReadyEvent(
            DateTimeOffset.UtcNow,
            "run-1",
            " ",
            "batch-1",
            Version: 1,
            Files: []);
        var publisher = new CapturingGatewayPublisher();
        var service = new FtpGatewayBackgroundService(
            new SingleBatchSource(batch),
            publisher,
            Options.Create(new GatewayOptions()),
            NullLogger<FtpGatewayBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(publisher.Feedback);
    }

    private sealed class SingleBatchSource(SenderFileBatchReadyEvent batch) : IGatewayBatchSource
    {
        public async IAsyncEnumerable<SenderFileBatchReadyEvent> ReadBatchReadyEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return batch;
            await Task.CompletedTask;
        }
    }

    private sealed class CapturingGatewayPublisher : IGatewayPublisher
    {
        private readonly ConcurrentQueue<SenderFileDispatchFeedback> _feedback = new();

        public IReadOnlyCollection<SenderFileDispatchFeedback> Feedback => _feedback.ToArray();

        public TaskCompletionSource<SenderFileDispatchFeedback> TerminalFeedback { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PublishFileBatchReadyAsync(SenderFileBatchReadyEvent @event, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PublishSenderFeedbackAsync(SenderFileDispatchFeedback feedback, CancellationToken cancellationToken)
        {
            _feedback.Enqueue(feedback);
            if (feedback.Kind is SenderFeedbackKind.BatchCompleted or SenderFeedbackKind.BatchFailed)
            {
                TerminalFeedback.TrySetResult(feedback);
            }

            return Task.CompletedTask;
        }
    }
}
