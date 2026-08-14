import {
  RequeueToGatewayResponse,
  SenderBatchStatus,
  SenderBatchStatusInfo,
} from '../../app.models';
import {
  GatewayDelivery,
  HistoryFileDelivery,
  HistoryFileRow,
  HistoryGatewayAttempt,
  HistoryTaskCode,
  RequeueFileSummary,
} from './history-projection.models';
import { extraFilePathKey, memberKey, normalizeFilePath } from './member-index.util';

export function buildAcceptedRequeuePaths(
  result: RequeueToGatewayResponse | null,
  snapshot: HistoryFileRow[] | null,
): Set<string> {
  if (!result?.results?.length || !snapshot?.length) {
    return new Set();
  }

  const sentPaths = new Set<string>();
  for (const itemResult of result.results) {
    const acceptedMembers = acceptedMemberNames(itemResult.batches ?? []);
    if (acceptedMembers.size === 0) {
      continue;
    }

    const correlationId = (itemResult.correlationId ?? '').trim();
    const taskCode = (itemResult.taskCode ?? '').trim().toLowerCase() as HistoryTaskCode;
    for (const row of snapshot) {
      if (
        row.correlationId === correlationId &&
        row.taskCode === taskCode &&
        acceptedMembers.has(memberKey(row.memberName))
      ) {
        sentPaths.add(row.filePath);
      }
    }
  }

  return sentPaths;
}

export function summarizeRequeue(
  result: RequeueToGatewayResponse,
  selected: HistoryFileRow[],
): RequeueFileSummary {
  const total = selected.length;
  if (!result?.results || total === 0) {
    return { total, accepted: 0, rejected: total };
  }

  const selectedByItemKey = new Map<string, HistoryFileRow[]>();
  for (const row of selected) {
    const key = `${row.taskCode}|${row.correlationId}`;
    selectedByItemKey.set(key, [...(selectedByItemKey.get(key) ?? []), row]);
  }

  let accepted = 0;
  for (const [itemKey, selectedRows] of selectedByItemKey) {
    const [taskCode, correlationId] = itemKey.split('|', 2) as [HistoryTaskCode, string];
    const itemResult = result.results.find(
      (item) =>
        (item.taskCode ?? '').trim().toLowerCase() === taskCode &&
        (item.correlationId ?? '').trim() === correlationId,
    );
    if (!itemResult) {
      continue;
    }

    const failed =
      Number(itemResult.failedBatches ?? 0) > 0 ||
      itemResult.batches.some((batch) => batch.status === SenderBatchStatus.Failed);
    if (failed) {
      continue;
    }

    const acceptedMembers = acceptedMemberNames(itemResult.batches ?? []);
    accepted += selectedRows.filter((row) => acceptedMembers.has(memberKey(row.memberName))).length;
  }

  return { total, accepted, rejected: Math.max(0, total - accepted) };
}

export function buildFileGatewayDeliveries(
  filePath: string,
  batches: SenderBatchStatusInfo[],
  extraPath = false,
): HistoryFileDelivery[] {
  const pathKey = extraPath ? extraFilePathKey : normalizeFilePath;
  const target = pathKey(filePath);
  const deliveries: HistoryFileDelivery[] = [];

  for (const batch of batches) {
    for (const sentFile of batch.sentFiles ?? []) {
      if (pathKey(sentFile.filePath) === target) {
        deliveries.push({ batchId: batch.batchId, sentAt: sentFile.sentAt });
      }
    }
  }

  return deliveries.sort((left, right) => Date.parse(right.sentAt) - Date.parse(left.sentAt));
}

export function buildGatewayAttempts(run: {
  senderBatches?: Record<string, SenderBatchStatusInfo> | null;
}): HistoryGatewayAttempt[] {
  return Object.values(run.senderBatches ?? {})
    .map((batch) => ({
      batchId: batch.batchId,
      memberName: batch.memberName,
      status: batch.status,
      updatedAt: batch.updatedAt,
      sentFileCount: batch.sentFiles?.length ?? 0,
      message: batch.message ?? null,
      repeated: batch.batchId.toLowerCase().startsWith('requeue-'),
    }))
    .sort((left, right) => Date.parse(right.updatedAt) - Date.parse(left.updatedAt));
}

export function resolveGatewayDelivery(
  publishToGateway: boolean,
  files: HistoryFileRow[],
  batches: SenderBatchStatusInfo[],
): GatewayDelivery {
  const deliveredFileCount = files.filter((file) => file.sentToGateway).length;
  const hasFailure = batches.some((batch) => batch.status === SenderBatchStatus.Failed);
  if (hasFailure) {
    return deliveredFileCount > 0 ? 'partial' : 'failed';
  }
  if (files.length > 0 && deliveredFileCount === files.length) {
    return 'delivered';
  }
  if (deliveredFileCount > 0) {
    return 'partial';
  }
  if (!publishToGateway) {
    return 'off';
  }
  return 'notSent';
}

function acceptedMemberNames(
  batches: Array<{ memberName: string; status: SenderBatchStatus }>,
): Set<string> {
  return new Set(
    batches
      .filter((batch) => batch.status !== SenderBatchStatus.Failed)
      .map((batch) => memberKey(batch.memberName))
      .filter((name) => name.length > 0),
  );
}
