import {
  RequeueToGatewayResponse,
  SenderBatchStatus,
  SenderBatchStatusInfo,
} from '../../app.models';
import {
  GatewayDelivery,
  HistoryFileRow,
  HistoryTaskCode,
  RequeueFileSummary,
} from './history-projection.models';
import { memberKey } from './member-index.util';

export function buildConfirmedSentPaths(
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
    return { total, sent: 0, notSent: total };
  }

  const selectedByItemKey = new Map<string, HistoryFileRow[]>();
  for (const row of selected) {
    const key = `${row.taskCode}|${row.correlationId}`;
    selectedByItemKey.set(key, [...(selectedByItemKey.get(key) ?? []), row]);
  }

  let sent = 0;
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
    sent += selectedRows.filter((row) => acceptedMembers.has(memberKey(row.memberName))).length;
  }

  return { total, sent, notSent: Math.max(0, total - sent) };
}

export function resolveGatewayDelivery(
  publishToGateway: boolean,
  files: HistoryFileRow[],
  batches: SenderBatchStatusInfo[],
): GatewayDelivery {
  if (!publishToGateway) {
    return 'off';
  }

  const hasFailure = batches.some((batch) => batch.status === SenderBatchStatus.Failed);
  if (!hasFailure) {
    return 'delivered';
  }

  return files.some((file) => file.sentToGateway) ? 'partial' : 'failed';
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
