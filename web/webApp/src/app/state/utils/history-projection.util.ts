import {
  OutputFileInfo,
  RequeueToGatewayResponse,
  RunStatusInfo,
  SenderBatchStatus,
  TaskRecord,
} from '../../app.models';
import { byDescDate } from './compare.util';
import { resolveRunStatusLabel } from './labels.util';
import { buildRunMemberIndex, isFileSentViaBatch, memberKey } from './member-index.util';
import { sortNames } from './sort.util';

export type HistoryTaskCode = 'run' | 'extra';

export interface HistoryFileRow {
  key: string;
  taskCode: HistoryTaskCode;
  correlationId: string;
  memberName: string;
  fileName: string;
  filePath: string;
  occurredAt: string;
  sentToGateway: boolean;
}

export interface HistoryRunNode {
  key: string;
  taskCode: HistoryTaskCode;
  correlationId: string;
  status: string;
  startedAt: string;
  completedAt: string;
  occurredAt: string;
  publishToGateway: boolean;
  memberNames: string[];
  memberFiles: Record<string, HistoryFileRow[]>;
}

export interface HistoryProjectionInput {
  todayRuns: RunStatusInfo[];
  todayHistory: TaskRecord[];
  allTodayRuns: RunStatusInfo[];
  outputFilesByPath: Record<string, OutputFileInfo[]>;
  knownMemberNames: string[];
  confirmedSentPaths: Set<string>;
}

export function buildHistoryNodes(input: HistoryProjectionInput): HistoryRunNode[] {
  const nodeMap = new Map<string, HistoryRunNode>();
  const { todayRuns, todayHistory, allTodayRuns, outputFilesByPath, knownMemberNames, confirmedSentPaths } = input;

  for (const run of todayRuns) {
    const correlationId = run.correlationId?.trim();
    if (!correlationId) {
      continue;
    }

    const memberFiles = buildRunMemberFiles(run, correlationId, confirmedSentPaths);
    nodeMap.set(`run|${correlationId}`, {
      key: `run|${correlationId}`,
      taskCode: 'run',
      correlationId,
      status: resolveRunStatusLabel(run.status),
      startedAt: run.createdAt,
      completedAt: run.updatedAt,
      occurredAt: run.updatedAt,
      publishToGateway: run.publishToGateway ?? true,
      memberNames: collectRunMemberNames(run, knownMemberNames),
      memberFiles,
    });
  }

  for (const record of todayHistory) {
    if (record.taskCode !== 'extra' || !record.correlationId || !record.outputPath) {
      continue;
    }

    const correlationId = record.correlationId;
    const files = outputFilesByPath[record.outputPath] ?? [];
    const extraRun = allTodayRuns.find(
      (run) => (run.correlationId ?? '').trim() === correlationId,
    );
    const extraIndex = buildRunMemberIndex(extraRun ?? null);

    const memberMap = new Map<string, HistoryFileRow[]>();
    for (const file of files) {
      const memberName = memberNameFromFile(file.fileName);
      const batch = extraIndex.batches.get(memberKey(memberName));
      const bucket = memberMap.get(memberName) ?? [];
      bucket.push({
        key: `extra|${correlationId}|${file.filePath}`,
        taskCode: 'extra',
        correlationId,
        memberName,
        fileName: file.fileName,
        filePath: file.filePath,
        occurredAt: file.modifiedAt || record.completedAt,
        sentToGateway:
          confirmedSentPaths.has(file.filePath) ||
          isFileSentViaBatch(file.filePath, batch?.sentFiles ?? null),
      });
      memberMap.set(memberName, bucket);
    }

    const memberFiles = Object.fromEntries(memberMap.entries());
    nodeMap.set(`extra|${correlationId}`, {
      key: `extra|${correlationId}`,
      taskCode: 'extra',
      correlationId,
      status: 'Completed',
      startedAt: record.startedAt,
      completedAt: record.completedAt,
      occurredAt: record.completedAt,
      publishToGateway: false,
      memberNames: sortNames([...knownMemberNames, ...Object.keys(memberFiles)]),
      memberFiles,
    });
  }

  return Array.from(nodeMap.values()).sort(byDescDate<HistoryRunNode>((node) => node.occurredAt));
}

export function buildConfirmedSentPaths(
  result: RequeueToGatewayResponse | null,
  snapshot: HistoryFileRow[] | null,
): Set<string> {
  if (!result?.results?.length || !snapshot?.length) {
    return new Set();
  }

  const sentPaths = new Set<string>();

  for (const itemResult of result.results) {
    const acceptedMembers = new Set(
      (itemResult.batches ?? [])
        .filter((batch) => batch.status !== SenderBatchStatus.Failed)
        .map((batch) => memberKey(batch.memberName))
        .filter((name) => name.length > 0),
    );
    if (acceptedMembers.size === 0) {
      continue;
    }

    const corrId = (itemResult.correlationId ?? '').trim();
    const code = (itemResult.taskCode ?? '').trim().toLowerCase() as HistoryTaskCode;

    for (const row of snapshot) {
      if (
        row.correlationId === corrId &&
        row.taskCode === code &&
        acceptedMembers.has(memberKey(row.memberName))
      ) {
        sentPaths.add(row.filePath);
      }
    }
  }

  return sentPaths;
}

export interface RequeueFileSummary {
  total: number;
  sent: number;
  notSent: number;
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
    const bucket = selectedByItemKey.get(key);
    if (bucket) {
      bucket.push(row);
    } else {
      selectedByItemKey.set(key, [row]);
    }
  }

  let sent = 0;
  for (const [itemKey, selectedRows] of selectedByItemKey.entries()) {
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
      (itemResult.failedBatches ?? 0) > 0 ||
      itemResult.batches.some((batch) => batch.status === SenderBatchStatus.Failed);
    if (failed) {
      continue;
    }

    const acceptedMembers = new Set(
      (itemResult.batches ?? [])
        .filter((batch) => batch.status !== SenderBatchStatus.Failed)
        .map((batch) => memberKey(batch.memberName))
        .filter((name) => name.length > 0),
    );

    for (const row of selectedRows) {
      if (acceptedMembers.has(memberKey(row.memberName))) {
        sent++;
      }
    }
  }

  return { total, sent, notSent: Math.max(0, total - sent) };
}

function buildRunMemberFiles(
  run: RunStatusInfo,
  correlationId: string,
  confirmedSentPaths: Set<string>,
): Record<string, HistoryFileRow[]> {
  const memberMap = new Map<string, HistoryFileRow[]>();
  const index = buildRunMemberIndex(run);

  for (const artifact of run.outputArtifacts ?? []) {
    if (!artifact.filePath || !artifact.fileName) {
      continue;
    }
    const memberName = artifact.memberName || 'GLOBAL';
    const batch = index.batches.get(memberKey(memberName));
    const bucket = memberMap.get(memberName) ?? [];
    bucket.push({
      key: `run|${correlationId}|${artifact.filePath}`,
      taskCode: 'run',
      correlationId,
      memberName,
      fileName: artifact.fileName,
      filePath: artifact.filePath,
      occurredAt: artifact.occurredAt || run.updatedAt,
      sentToGateway:
        confirmedSentPaths.has(artifact.filePath) ||
        isFileSentViaBatch(artifact.filePath, batch?.sentFiles ?? null),
    });
    memberMap.set(memberName, bucket);
  }

  return Object.fromEntries(memberMap.entries());
}

function collectRunMemberNames(run: RunStatusInfo, knownMemberNames: string[]): string[] {
  const names = new Set<string>(knownMemberNames);
  for (const status of Object.values(run.memberStatuses ?? {})) {
    if (status.memberName?.trim()) names.add(status.memberName);
  }
  for (const batch of Object.values(run.senderBatches ?? {})) {
    if (batch.memberName?.trim()) names.add(batch.memberName);
  }
  for (const artifact of run.outputArtifacts ?? []) {
    if (artifact.memberName?.trim()) names.add(artifact.memberName);
  }
  return sortNames(names);
}

function memberNameFromFile(fileName: string): string {
  if (!fileName) {
    return 'UNKNOWN';
  }
  const dotIndex = fileName.lastIndexOf('.');
  return dotIndex > 0 ? fileName.slice(0, dotIndex) : fileName;
}
