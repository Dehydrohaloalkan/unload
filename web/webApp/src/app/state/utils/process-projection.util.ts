import {
  FileRunStage,
  FileRunStatusInfo,
  MemberRunLifecycleStatus,
  MemberRunStatusInfo,
  RunStatusInfo,
  ScriptRunStage,
  ScriptRunStatusInfo,
  SenderBatchStatus,
  SenderBatchStatusInfo,
} from '../../app.models';
import {
  ProcessActiveItemSummary,
  ProcessBatchCard,
  ProcessFileCard,
  ProcessMemberRow,
  ProcessScriptCard,
} from './process-projection.models';

/**
 * Projects one server snapshot into the vertically scrollable process rows.
 * `now` is deliberately supplied by the caller so the result is deterministic
 * in both the UI and tests; active durations use this instant.
 */
export function buildProcessMemberRows(
  run: RunStatusInfo | null | undefined,
  now: Date,
): ProcessMemberRow[] {
  if (!run) {
    return [];
  }

  const names = new Map<string, string[]>();
  const memberStatuses = new Map<string, MemberRunStatusInfo>();
  const scripts = new Map<string, ProcessScriptCard>();
  const files = new Map<string, ProcessFileCard>();
  const batches = new Map<string, ProcessBatchCard>();

  for (const [mapKey, status] of Object.entries(run.memberStatuses ?? {})) {
    if (!status) continue;
    const memberName = cleanString(status.memberName) || cleanString(mapKey);
    addMemberName(names, memberName);
    const key = memberKey(memberName);
    if (!key) continue;

    const existing = memberStatuses.get(key);
    if (!existing || preferUpdated(status.updatedAt, existing.updatedAt)) {
      memberStatuses.set(key, status);
    }
  }

  for (const [mapKey, status] of Object.entries(run.scriptStatuses ?? {})) {
    if (!status) continue;
    const memberName = cleanString(status.memberName) || cleanString(mapKey);
    addMemberName(names, memberName);

    const id = cleanString(status.id) || cleanString(mapKey);
    if (!id) continue;
    const card = toScriptCard(status, id, memberName, now);
    const existing = scripts.get(card.id.toLowerCase());
    if (!existing || preferUpdated(card.updatedAt, existing.updatedAt)) {
      scripts.set(card.id.toLowerCase(), card);
    }
  }

  for (const [mapKey, status] of Object.entries(run.fileStatuses ?? {})) {
    if (!status) continue;
    const memberName = cleanString(status.memberName) || cleanString(mapKey);
    addMemberName(names, memberName);

    const id = cleanString(status.id) || cleanString(mapKey);
    if (!id) continue;
    const card = toFileCard(status, id, memberName, now);
    const existing = files.get(card.id.toLowerCase());
    if (!existing || preferUpdated(card.updatedAt, existing.updatedAt)) {
      files.set(card.id.toLowerCase(), card);
    }
  }

  for (const [mapKey, status] of Object.entries(run.senderBatches ?? {})) {
    if (!status) continue;
    const memberName = cleanString(status.memberName) || cleanString(mapKey);
    addMemberName(names, memberName);

    const id = cleanString(status.batchId) || cleanString(mapKey);
    if (!id) continue;
    const card = toBatchCard(status, id, memberName, now);
    const existing = batches.get(card.id.toLowerCase());
    if (!existing || preferUpdated(card.updatedAt, existing.updatedAt)) {
      batches.set(card.id.toLowerCase(), card);
    }
  }

  const filesByParent = new Map<string, ProcessFileCard[]>();
  for (const file of files.values()) {
    const parentKey = memberKey(file.parentScriptId);
    const bucket = filesByParent.get(parentKey) ?? [];
    bucket.push(file);
    filesByParent.set(parentKey, bucket);
  }

  for (const script of scripts.values()) {
    script.files = sortFiles(filesByParent.get(memberKey(script.id)) ?? []);
  }

  const scriptsByMember = groupByMember(scripts.values());
  const filesByMember = groupByMember(files.values());
  const batchesByMember = groupByMember(batches.values());

  const rows = Array.from(names.entries()).map(([key, candidates]) => {
    const status = memberStatuses.get(key);
    const rowScripts = sortScripts([...(scriptsByMember.get(key) ?? [])]);
    const rowFiles = [...(filesByMember.get(key) ?? [])];
    const rowBatches = sortBatches([...(batchesByMember.get(key) ?? [])]);
    const knownFileIds = new Set(
      rowScripts.flatMap((script) => script.files.map((file) => file.id.toLowerCase())),
    );
    const orphanFiles = sortFiles(
      rowFiles.filter((file) => !knownFileIds.has(file.id.toLowerCase())),
    );
    const activeItemSummary = buildActiveSummary(rowScripts, orphanFiles, rowBatches, now);

    return {
      key,
      name: pickDisplayName(candidates),
      status: normalizeMemberStatus(status?.status),
      updatedAt: latestTimestamp([
        status?.updatedAt,
        ...rowScripts.map((script) => script.updatedAt),
        ...rowFiles.map((file) => file.updatedAt),
        ...rowBatches.map((batch) => batch.updatedAt),
      ]),
      scripts: rowScripts,
      orphanFiles,
      batches: rowBatches,
      activeItemSummary,
      oldestActiveAt: activeItemSummary.oldestAt,
    } satisfies ProcessMemberRow;
  });

  return rows.sort(
    (left, right) => compareText(left.name, right.name) || compareText(left.key, right.key),
  );
}

function groupByMember<T extends { memberName: string }>(values: Iterable<T>): Map<string, T[]> {
  const grouped = new Map<string, T[]>();
  for (const value of values) {
    const key = memberKey(value.memberName);
    const bucket = grouped.get(key) ?? [];
    bucket.push(value);
    grouped.set(key, bucket);
  }
  return grouped;
}

function toScriptCard(
  source: ScriptRunStatusInfo,
  id: string,
  memberName: string,
  now: Date,
): ProcessScriptCard {
  const discoveredAt = cleanTimestamp(source.discoveredAt);
  const stageEnteredAt = cleanTimestamp(source.stageEnteredAt);
  const startedAt = cleanTimestamp(source.startedAt);
  const updatedAt = cleanTimestamp(source.updatedAt);
  const completedAt = cleanTimestamp(source.completedAt);
  const stage = normalizeEnum(source.stage);
  const terminal = isTerminalScriptStage(stage);
  const executionStartedAt =
    stage === ScriptRunStage.AwaitingWorker
      ? (stageEnteredAt ?? discoveredAt)
      : (startedAt ?? stageEnteredAt);

  return {
    id,
    memberName,
    scriptCode: cleanString(source.scriptCode),
    stage,
    workerId: toNumber(source.workerId),
    records: toNumber(source.records),
    discoveredAt,
    stageEnteredAt,
    startedAt,
    updatedAt,
    completedAt,
    queueWaitMs: duration(discoveredAt, startedAt ?? (terminal ? (completedAt ?? updatedAt) : now)),
    stageElapsedMs: duration(executionStartedAt, terminal ? (completedAt ?? updatedAt) : now),
    files: [],
  };
}

function toFileCard(
  source: FileRunStatusInfo,
  id: string,
  memberName: string,
  now: Date,
): ProcessFileCard {
  const queuedAt = cleanTimestamp(source.queuedAt);
  const stageEnteredAt = cleanTimestamp(source.stageEnteredAt);
  const updatedAt = cleanTimestamp(source.updatedAt);
  const completedAt = cleanTimestamp(source.completedAt);
  const stage = normalizeEnum(source.stage);
  const terminal = isTerminalFileStage(stage);

  return {
    id,
    memberName,
    parentScriptId: cleanString(source.parentScriptId),
    chunk: toNumber(source.chunkNumber),
    stage,
    fileName: cleanString(source.fileName) || null,
    path: cleanString(source.filePath) || null,
    rows: toNumber(source.rows),
    bytes: toNumber(source.estimatedBytes),
    queuedAt,
    stageEnteredAt,
    updatedAt,
    completedAt,
    workerId: toNumber(source.workerId),
    writeElapsedMs: duration(queuedAt, terminal ? (completedAt ?? updatedAt) : now),
  };
}

function toBatchCard(
  source: SenderBatchStatusInfo,
  id: string,
  memberName: string,
  now: Date,
): ProcessBatchCard {
  const queuedAt = cleanTimestamp(source.queuedAt);
  const startedAt = cleanTimestamp(source.startedAt);
  const updatedAt = cleanTimestamp(source.updatedAt);
  const status = normalizeEnum(source.status);
  const terminal = isTerminalBatchStatus(status);
  const sentFiles = Array.isArray(source.sentFiles) ? source.sentFiles : [];

  return {
    id,
    memberName,
    status,
    queuedAt,
    startedAt,
    updatedAt,
    fileCount: toNumber(source.fileCount),
    sentCount: sentFiles.length,
    queueWaitMs: duration(queuedAt, startedAt ?? (terminal ? updatedAt : now)),
    sendElapsedMs: duration(startedAt, terminal ? updatedAt : now),
  };
}

function buildActiveSummary(
  scripts: ProcessScriptCard[],
  orphanFiles: ProcessFileCard[],
  batches: ProcessBatchCard[],
  now: Date,
): ProcessActiveItemSummary {
  const activeScripts = scripts.filter((script) => isActiveScriptStage(script.stage));
  const allFiles = [...scripts.flatMap((script) => script.files), ...orphanFiles];
  const activeFiles = allFiles.filter((file) => isActiveFileStage(file.stage));
  const activeBatches = batches.filter((batch) => isActiveBatchStatus(batch.status));
  const activeTimestamps = [
    ...activeScripts.map((script) => script.stageEnteredAt),
    ...activeFiles.map((file) => file.stageEnteredAt),
    ...activeBatches.map((batch) => batch.startedAt ?? batch.queuedAt),
  ].filter((timestamp): timestamp is string => !!timestamp && toEpoch(timestamp) !== null);
  const oldestAt = activeTimestamps.length
    ? activeTimestamps.reduce((oldest, current) =>
        (toEpoch(current) ?? Number.POSITIVE_INFINITY) <
        (toEpoch(oldest) ?? Number.POSITIVE_INFINITY)
          ? current
          : oldest,
      )
    : null;

  return {
    count: activeScripts.length + activeFiles.length + activeBatches.length,
    scriptCount: activeScripts.length,
    fileCount: activeFiles.length,
    batchCount: activeBatches.length,
    oldestAt,
    oldestElapsedMs: oldestAt ? duration(oldestAt, now) : null,
  };
}

function addMemberName(names: Map<string, string[]>, value: string): void {
  const name = value.trim();
  const key = memberKey(name);
  if (!key) return;
  const candidates = names.get(key) ?? [];
  if (!candidates.includes(name)) candidates.push(name);
  names.set(key, candidates);
}

function pickDisplayName(candidates: string[]): string {
  return [...candidates].sort(compareText)[0] ?? '';
}

function sortScripts(cards: ProcessScriptCard[]): ProcessScriptCard[] {
  return cards.sort(
    (left, right) =>
      compareText(left.scriptCode, right.scriptCode) || compareText(left.id, right.id),
  );
}

function sortFiles(cards: ProcessFileCard[]): ProcessFileCard[] {
  return cards.sort(
    (left, right) =>
      (left.chunk ?? Number.POSITIVE_INFINITY) - (right.chunk ?? Number.POSITIVE_INFINITY) ||
      compareText(left.fileName ?? left.path ?? '', right.fileName ?? right.path ?? '') ||
      compareText(left.id, right.id),
  );
}

function sortBatches(cards: ProcessBatchCard[]): ProcessBatchCard[] {
  return cards.sort(
    (left, right) =>
      compareNullableTimestamp(left.queuedAt, right.queuedAt) || compareText(left.id, right.id),
  );
}

function compareNullableTimestamp(left: string | null, right: string | null): number {
  const leftTime = left ? (toEpoch(left) ?? Number.POSITIVE_INFINITY) : Number.POSITIVE_INFINITY;
  const rightTime = right ? (toEpoch(right) ?? Number.POSITIVE_INFINITY) : Number.POSITIVE_INFINITY;
  return leftTime - rightTime;
}

function compareText(left: string, right: string): number {
  return (
    left.localeCompare(right, undefined, { numeric: true, sensitivity: 'base' }) ||
    left.localeCompare(right)
  );
}

function memberKey(value: string): string {
  return value.trim().toLowerCase();
}

function cleanString(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

function cleanTimestamp(value: unknown): string | null {
  const timestamp = cleanString(value);
  return timestamp && toEpoch(timestamp) !== null ? timestamp : null;
}

function latestTimestamp(values: Array<string | null | undefined>): string | null {
  return (
    values
      .map((value) => cleanTimestamp(value))
      .filter((value): value is string => !!value)
      .sort((left, right) => (toEpoch(right) ?? 0) - (toEpoch(left) ?? 0))[0] ?? null
  );
}

function preferUpdated(candidate: unknown, current: unknown): boolean {
  const candidateTime = toEpoch(cleanString(candidate));
  const currentTime = toEpoch(cleanString(current));
  if (candidateTime !== null && currentTime === null) return true;
  if (candidateTime === null || currentTime === null) return false;
  return candidateTime > currentTime;
}

function toEpoch(value: string | null | undefined): number | null {
  if (!value) return null;
  const epoch = Date.parse(value);
  return Number.isFinite(epoch) ? epoch : null;
}

function duration(start: string | null, end: string | Date | null): number | null {
  const startEpoch = toEpoch(start);
  if (startEpoch === null) return null;
  const endEpoch = end instanceof Date ? end.getTime() : toEpoch(end);
  if (endEpoch === null || !Number.isFinite(endEpoch)) return null;
  return Math.max(0, endEpoch - startEpoch);
}

function toNumber(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined) return null;
  if (typeof value === 'string' && value.trim() === '') return null;
  const result = typeof value === 'number' ? value : Number(value.trim());
  return Number.isFinite(result) ? result : null;
}

function normalizeEnum(value: number | string | null | undefined): number | null {
  return toNumber(value);
}

function normalizeMemberStatus(value: number | null | undefined): MemberRunLifecycleStatus | null {
  return normalizeEnum(value);
}

function isTerminalScriptStage(stage: ScriptRunStage | null): boolean {
  return (
    stage === ScriptRunStage.Completed ||
    stage === ScriptRunStage.Failed ||
    stage === ScriptRunStage.Cancelled
  );
}

function isActiveScriptStage(stage: ScriptRunStage | null): boolean {
  return stage === ScriptRunStage.AwaitingWorker || stage === ScriptRunStage.Running;
}

function isTerminalFileStage(stage: FileRunStage | null): boolean {
  return (
    stage === FileRunStage.Written ||
    stage === FileRunStage.Failed ||
    stage === FileRunStage.Cancelled
  );
}

function isActiveFileStage(stage: FileRunStage | null): boolean {
  return stage === FileRunStage.QueuedForWrite;
}

function isTerminalBatchStatus(status: SenderBatchStatus | null): boolean {
  return (
    status === SenderBatchStatus.Completed ||
    status === SenderBatchStatus.Failed ||
    status === SenderBatchStatus.SkippedByRequest
  );
}

function isActiveBatchStatus(status: SenderBatchStatus | null): boolean {
  return status === SenderBatchStatus.Ready || status === SenderBatchStatus.InProgress;
}
