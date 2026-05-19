using Unload.Core;

namespace Unload.Store;

public record RequeueToGatewayRequest(
    string? IdempotencyKey,
    IReadOnlyCollection<RequeueItem> Items,
    bool DryRun = false);

public record RequeueItem(
    string TaskCode,
    string CorrelationId,
    IReadOnlyCollection<string>? MemberNames = null,
    IReadOnlyCollection<string>? FilePaths = null);

public record RequeueToGatewayResponse(
    string RequestId,
    int AcceptedBatches,
    int FailedBatches,
    IReadOnlyCollection<RequeueItemResult> Results);

public record RequeueItemResult(
    string TaskCode,
    string CorrelationId,
    int AcceptedBatches,
    int FailedBatches,
    IReadOnlyCollection<RequeueBatchResult> Batches);

public record RequeueBatchResult(
    string MemberName,
    string BatchId,
    SenderBatchStatus Status,
    string? Message = null);
