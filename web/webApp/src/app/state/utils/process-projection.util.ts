import {
  FileRunStage,
  FileRunStatusInfo,
  MemberRunLifecycleStatus,
  MemberRunStatusInfo,
  RunnerFailureInfo,
  RunStatusInfo,
  RunWorkerStatusInfo,
  ScriptRunStage,
  ScriptRunStatusInfo,
  SenderBatchStatus,
  SenderBatchStatusInfo,
} from '../../app.models';
import {
  ProcessBatchCard,
  ProcessDispatchFile,
  ProcessEntityReference,
  ProcessEntityType,
  ProcessFailureDetail,
  ProcessFileCard,
  ProcessFileGroup,
  ProcessFileStatusCounts,
  ProcessMemberCard,
  ProcessMemberIdentity,
  ProcessPipelineViewModel,
  ProcessScriptCard,
  ProcessWorkerSlot,
} from './process-projection.models';

export const PROCESS_WORKER_COUNT = 4;
export const PROCESS_PAGE_SIZE = 20;
const MEMBER_IDENTITY_COUNT = 8;

const EMPTY_PIPELINE: ProcessPipelineViewModel = Object.freeze({
  correlationId: '',
  members: Object.freeze([]),
  scripts: Object.freeze([]),
  memberInput: Object.freeze([]),
  resolver: Object.freeze([]),
  scriptQueue: Object.freeze([]),
  workers: Object.freeze([]),
  fileGroups: Object.freeze([]),
  senderBatches: Object.freeze([]),
  senderQueue: Object.freeze([]),
  senderInProgress: Object.freeze([]),
  delivered: Object.freeze([]),
  failures: Object.freeze([]),
});

/**
 * Builds the immutable, clock-independent state consumed by the vertical Process UI.
 * QueuePosition/WorkOrder/Sequence are authoritative; names and ids are only deterministic fallbacks.
 */
export function buildProcessPipeline(
  run: RunStatusInfo | null | undefined,
): ProcessPipelineViewModel {
  if (!run) return EMPTY_PIPELINE;

  const memberNames = collectMemberNames(run);
  const identities = new Map(
    memberNames.map((name) => [memberKey(name), resolveProcessMemberIdentity(name)]),
  );
  const identityFor = (name: string) =>
    identities.get(memberKey(name)) ?? resolveProcessMemberIdentity(name);

  const members = deduplicateStatuses(run.memberStatuses, (status, mapKey) =>
    memberKey(cleanString(status.memberName) || mapKey),
  ).map(([mapKey, status]) => toMemberCard(status, mapKey, identityFor));
  const scripts = deduplicateStatuses(run.scriptStatuses, (status, mapKey) =>
    (cleanString(status.id) || mapKey).toLowerCase(),
  ).map(([mapKey, status]) => toScriptCard(status, mapKey, identityFor));
  const files = deduplicateStatuses(run.fileStatuses, (status, mapKey) =>
    (cleanString(status.id) || mapKey).toLowerCase(),
  ).map(([mapKey, status]) => toFileCard(status, mapKey, identityFor));
  const batches = deduplicateStatuses(run.senderBatches, (status, mapKey) =>
    (cleanString(status.batchId) || mapKey).toLowerCase(),
  ).map(([mapKey, status]) => toBatchCard(status, mapKey, identityFor, run.createdAt, files));

  const downstreamMembers = new Set(
    [...scripts, ...files, ...batches].map((item) => memberKey(item.memberName)),
  );
  const orderedMembers = sortMembers(members);
  const orderedScripts = sortScripts(scripts);
  const orderedBatches = assignDispatchOwnership(sortBatches(batches));
  const dispatchedPaths = new Set(
    orderedBatches
      .flatMap((batch) => [...batch.senderFiles, ...batch.sentFiles])
      .map((file) => file.id),
  );
  const createdFiles = files.filter(
    (file) =>
      file.stage === FileRunStage.Failed ||
      file.stage === FileRunStage.Cancelled ||
      !file.path ||
      !dispatchedPaths.has(filePathKey(file.path)),
  );
  const workers = buildWorkerSlots(run.workerStatuses, orderedScripts);
  const failures = collectFailures(
    run,
    orderedMembers,
    orderedScripts,
    files,
    orderedBatches,
    workers,
  );

  return Object.freeze({
    correlationId: run.correlationId,
    members: Object.freeze(orderedMembers),
    scripts: Object.freeze(orderedScripts),
    memberInput: Object.freeze(orderedMembers.filter((member) => member.zone === 'input')),
    resolver: Object.freeze(
      orderedMembers.filter(
        (member) =>
          member.zone === 'resolver' &&
          (member.status === MemberRunLifecycleStatus.Failed ||
            member.failure !== null ||
            !downstreamMembers.has(member.key)),
      ),
    ),
    scriptQueue: Object.freeze(orderedScripts.filter((script) => script.zone === 'script-queue')),
    workers: Object.freeze(workers),
    fileGroups: Object.freeze(buildFileGroups(createdFiles)),
    senderBatches: Object.freeze(orderedBatches.filter(needsDeliveryConfirmation)),
    senderQueue: Object.freeze(
      orderedBatches.filter(
        (batch) => batch.zone === 'sender-queue' && needsDeliveryConfirmation(batch),
      ),
    ),
    senderInProgress: Object.freeze(
      orderedBatches.filter(
        (batch) => batch.zone === 'sender-in-progress' && needsDeliveryConfirmation(batch),
      ),
    ),
    delivered: Object.freeze(orderedBatches.filter((batch) => batch.sentFiles.length > 0)),
    failures: Object.freeze(failures),
  });
}

/** Stable palette selection. The class is deliberately a token, not an inline/random color. */
export function resolveProcessMemberIdentity(memberName: string): ProcessMemberIdentity {
  let hash = 2166136261;
  for (const char of memberKey(memberName)) {
    hash ^= char.charCodeAt(0);
    hash = Math.imul(hash, 16777619);
  }
  const index = (hash >>> 0) % MEMBER_IDENTITY_COUNT;
  return Object.freeze({
    index,
    token: `member-${index}`,
    className: `process-member-accent--${index}`,
  });
}

function collectMemberNames(run: RunStatusInfo): string[] {
  const names = new Map<string, string[]>();
  const add = (value: unknown) => {
    const name = cleanString(value);
    const key = memberKey(name);
    if (!key) return;
    const candidates = names.get(key) ?? [];
    if (!candidates.includes(name)) candidates.push(name);
    names.set(key, candidates);
  };
  Object.entries(run.memberStatuses ?? {}).forEach(([key, value]) => add(value?.memberName || key));
  Object.entries(run.scriptStatuses ?? {}).forEach(([key, value]) => add(value?.memberName || key));
  Object.entries(run.fileStatuses ?? {}).forEach(([key, value]) => add(value?.memberName || key));
  Object.entries(run.senderBatches ?? {}).forEach(([key, value]) => add(value?.memberName || key));
  Object.values(run.workerStatuses ?? {}).forEach((value) => add(value?.memberName));
  return [...names.values()].map((values) => [...values].sort(compareText)[0]);
}

function deduplicateStatuses<
  T extends { sequence?: number | string | null; updatedAt?: string | null },
>(
  source: { [key: string]: T } | null | undefined,
  keyOf: (status: T, mapKey: string) => string,
): Array<[string, T]> {
  const result = new Map<string, [string, T]>();
  for (const [mapKey, status] of Object.entries(source ?? {})) {
    if (!status) continue;
    const key = keyOf(status, mapKey);
    if (!key) continue;
    const existing = result.get(key);
    if (!existing || compareSnapshotRecency(existing[1], status) < 0)
      result.set(key, [mapKey, status]);
  }
  return [...result.values()];
}

function compareSnapshotRecency(
  left: { sequence?: number | string | null; updatedAt?: string | null },
  right: { sequence?: number | string | null; updatedAt?: string | null },
): number {
  const leftSequence = toNumber(left.sequence);
  const rightSequence = toNumber(right.sequence);
  if (leftSequence !== null || rightSequence !== null) {
    if (leftSequence === null) return -1;
    if (rightSequence === null) return 1;
    if (leftSequence !== rightSequence) return leftSequence - rightSequence;
  }
  return (toEpoch(left.updatedAt) ?? 0) - (toEpoch(right.updatedAt) ?? 0);
}

function toMemberCard(
  source: MemberRunStatusInfo,
  mapKey: string,
  identityFor: (name: string) => ProcessMemberIdentity,
): ProcessMemberCard {
  const name = cleanString(source.memberName) || cleanString(mapKey);
  const status = normalizeEnum(source.status);
  return Object.freeze({
    key: memberKey(name),
    name,
    status,
    zone:
      status === MemberRunLifecycleStatus.Pending
        ? 'input'
        : status === MemberRunLifecycleStatus.Completed
          ? 'completed'
          : 'resolver',
    queuePosition: toNumber(source.queuePosition),
    sequence: toNumber(source.sequence),
    updatedAt: cleanTimestamp(source.updatedAt),
    failure: toFailure(source.failure, reference('member', name, name)),
    identity: identityFor(name),
  });
}

function toScriptCard(
  source: ScriptRunStatusInfo,
  mapKey: string,
  identityFor: (name: string) => ProcessMemberIdentity,
): ProcessScriptCard {
  const id = cleanString(source.id) || cleanString(mapKey);
  const memberName = cleanString(source.memberName);
  const scriptCode = cleanString(source.scriptCode);
  const stage = normalizeEnum(source.stage);
  const startedAt = cleanTimestamp(source.startedAt);
  const completedAt = cleanTimestamp(source.completedAt);
  const updatedAt = cleanTimestamp(source.updatedAt);
  const workerId = toNumber(source.workerId);
  const zone =
    stage === ScriptRunStage.AwaitingWorker ||
    (stage === ScriptRunStage.Failed && workerId === null && !startedAt)
      ? 'script-queue'
      : stage === ScriptRunStage.Running ||
          (stage === ScriptRunStage.Failed && (workerId !== null || !!startedAt))
        ? 'worker'
        : 'completed';
  return Object.freeze({
    id,
    memberName,
    scriptCode,
    stage,
    zone,
    workOrder: toNumber(source.workOrder),
    sequence: toNumber(source.sequence),
    workerId,
    records: toNumber(source.records),
    discoveredAt: cleanTimestamp(source.discoveredAt),
    stageEnteredAt: cleanTimestamp(source.stageEnteredAt),
    startedAt,
    updatedAt,
    completedAt,
    failure: toFailure(source.failure, reference('script', id, memberName, scriptCode, workerId)),
    identity: identityFor(memberName),
  });
}

function toFileCard(
  source: FileRunStatusInfo,
  mapKey: string,
  identityFor: (name: string) => ProcessMemberIdentity,
): ProcessFileCard {
  const id = cleanString(source.id) || cleanString(mapKey);
  const memberName = cleanString(source.memberName);
  const stage = normalizeEnum(source.stage);
  return Object.freeze({
    id,
    memberName,
    scriptCode: cleanString(source.scriptCode),
    parentScriptId: cleanString(source.parentScriptId),
    chunk: toNumber(source.chunkNumber),
    sequence: toNumber(source.sequence),
    stage,
    fileName: cleanString(source.fileName) || null,
    path: cleanString(source.filePath) || null,
    rows: toNumber(source.rows),
    bytes: toNumber(source.estimatedBytes),
    queuedAt: cleanTimestamp(source.queuedAt),
    stageEnteredAt: cleanTimestamp(source.stageEnteredAt),
    updatedAt: cleanTimestamp(source.updatedAt),
    completedAt: cleanTimestamp(source.completedAt),
    workerId: toNumber(source.workerId),
    failure: toFailure(
      source.failure,
      reference('file', id, memberName, cleanString(source.scriptCode), toNumber(source.workerId)),
    ),
    identity: identityFor(memberName),
  });
}

function toBatchCard(
  source: SenderBatchStatusInfo,
  mapKey: string,
  identityFor: (name: string) => ProcessMemberIdentity,
  runCreatedAt: string,
  canonicalFiles: readonly ProcessFileCard[],
): ProcessBatchCard {
  const id = cleanString(source.batchId) || cleanString(mapKey);
  const memberName = cleanString(source.memberName);
  const status = normalizeEnum(source.status);
  const queuedAt = cleanTimestamp(source.queuedAt);
  const startedAt = cleanTimestamp(source.startedAt);
  const updatedAt = cleanTimestamp(source.updatedAt);
  const identity = identityFor(memberName);
  const canonicalByPath = new Map(
    canonicalFiles
      .filter((file) => !!file.path)
      .map((file) => [filePathKey(file.path!), file]),
  );
  const sentByPath = new Map(
    (source.sentFiles ?? []).map((file) => [
      filePathKey(file.filePath),
      cleanTimestamp(file.sentAt),
    ]),
  );
  const planned = source.plannedFiles ?? null;
  const dispatchFiles: ProcessDispatchFile[] = planned
    ? uniqueBy(planned, (file) => filePathKey(file.filePath)).map((file) => {
        const path = cleanString(file.filePath);
        const sentAt = cleanTimestamp(file.sentAt) ?? sentByPath.get(filePathKey(path)) ?? null;
        return Object.freeze({
          id: filePathKey(path),
          batchId: id,
          memberName,
          fileName: cleanString(file.fileName) || basename(path),
          path,
          queuedAt: cleanTimestamp(file.queuedAt),
          sentAt,
          estimatedBytes: toNumber(file.estimatedBytes),
          actualBytes: toNumber(file.actualBytes),
          identity,
        });
      })
    : uniqueBy(source.sentFiles ?? [], (file) => filePathKey(file.filePath)).map((file) => {
        const path = cleanString(file.filePath);
        const canonical = canonicalByPath.get(filePathKey(path));
        return Object.freeze({
          id: filePathKey(path),
          batchId: id,
          memberName: canonical?.memberName || memberName,
          fileName: canonical?.fileName || basename(path),
          path,
          queuedAt,
          sentAt: cleanTimestamp(file.sentAt),
          estimatedBytes: canonical?.bytes ?? null,
          actualBytes: null,
          identity: canonical?.identity ?? identity,
        });
      });
  const senderFiles = Object.freeze(dispatchFiles.filter((file) => !file.sentAt));
  const sentFiles = Object.freeze(dispatchFiles.filter((file) => !!file.sentAt));
  const declaredCount = toNumber(source.fileCount);
  const zone =
    status === SenderBatchStatus.Completed || status === SenderBatchStatus.SkippedByRequest
      ? 'delivered'
      : status === SenderBatchStatus.InProgress ||
          (status === SenderBatchStatus.Failed && !!startedAt)
        ? 'sender-in-progress'
        : 'sender-queue';
  return Object.freeze({
    id,
    memberName,
    status,
    zone,
    sequence: toNumber(source.sequence),
    queuedAt,
    startedAt,
    updatedAt,
    fileCount: declaredCount,
    sentCount: sentFiles.length,
    unsentCount: senderFiles.length,
    hasLegacyUnsentAmbiguity:
      planned === null && declaredCount !== null && declaredCount > sentFiles.length,
    senderFiles,
    sentFiles,
    totalElapsedMs: duration(cleanTimestamp(runCreatedAt), sentFiles.length ? updatedAt : null),
    failure: toFailure(source.failure, reference('batch', id, memberName, null, null, id)),
    identity,
  });
}

/** A path can be requeued, but it must be represented by only one batch in the UI. */
function assignDispatchOwnership(batches: ProcessBatchCard[]): ProcessBatchCard[] {
  const owner = new Map<
    string,
    { batchId: string; sequence: number | null; updatedAt: string | null }
  >();
  for (const batch of batches) {
    for (const file of [...batch.senderFiles, ...batch.sentFiles]) {
      const candidate = {
        batchId: batch.id,
        sequence: batch.sequence,
        updatedAt: batch.updatedAt,
      };
      const current = owner.get(file.id);
      if (!current || compareDispatchOwner(current, candidate) < 0) owner.set(file.id, candidate);
    }
  }
  return batches.map((batch) => {
    const senderFiles = Object.freeze(
      batch.senderFiles.filter((file) => owner.get(file.id)?.batchId === batch.id),
    );
    const sentFiles = Object.freeze(
      batch.sentFiles.filter((file) => owner.get(file.id)?.batchId === batch.id),
    );
    return Object.freeze({
      ...batch,
      senderFiles,
      sentFiles,
      unsentCount: senderFiles.length,
      sentCount: sentFiles.length,
    });
  });
}

function compareDispatchOwner(
  left: { sequence: number | null; updatedAt: string | null; batchId: string },
  right: { sequence: number | null; updatedAt: string | null; batchId: string },
): number {
  if (left.sequence !== null && right.sequence !== null && left.sequence !== right.sequence) {
    return left.sequence - right.sequence;
  }
  return (
    (toEpoch(left.updatedAt) ?? 0) - (toEpoch(right.updatedAt) ?? 0) ||
    compareText(left.batchId, right.batchId)
  );
}

function needsDeliveryConfirmation(batch: ProcessBatchCard): boolean {
  if (batch.status === SenderBatchStatus.SkippedByRequest && !batch.senderFiles.length) return false;
  return batch.senderFiles.length > 0 || batch.hasLegacyUnsentAmbiguity || batch.failure !== null;
}

function buildWorkerSlots(
  statuses: { [key: string]: RunWorkerStatusInfo } | null | undefined,
  scripts: ProcessScriptCard[],
): ProcessWorkerSlot[] {
  const statusById = new Map<number, RunWorkerStatusInfo>();
  Object.values(statuses ?? {}).forEach((status) => {
    const id = toNumber(status?.workerId);
    if (status && id !== null && id >= 1 && id <= PROCESS_WORKER_COUNT) statusById.set(id, status);
  });
  return Array.from({ length: PROCESS_WORKER_COUNT }, (_, index) => {
    const workerId = index + 1;
    const status = statusById.get(workerId);
    const assigned = scripts.filter(
      (script) => script.workerId === workerId && script.zone === 'worker',
    );
    const active = assigned.find((script) => script.stage === ScriptRunStage.Running) ?? null;
    const failed = assigned.filter((script) => script.stage === ScriptRunStage.Failed);
    return Object.freeze({
      workerId,
      state: cleanString(status?.state) || (active ? 'Running' : 'Idle'),
      assignment: active,
      retainedFailures: Object.freeze(sortScripts(failed)),
      sequence: toNumber(status?.sequence),
      failure: toFailure(
        status?.failure,
        reference(
          'worker',
          String(workerId),
          cleanString(status?.memberName),
          cleanString(status?.scriptCode),
          workerId,
        ),
      ),
    });
  });
}

function buildFileGroups(files: ProcessFileCard[]): ProcessFileGroup[] {
  const grouped = groupBy(
    files,
    (file) => `${memberKey(file.memberName)}\u0000${file.parentScriptId.toLowerCase()}`,
  );
  return [...grouped.entries()]
    .map(([id, unsorted]) => {
      const details = Object.freeze(sortFiles([...unsorted]));
      const first = details[0];
      const statusCounts: ProcessFileStatusCounts = {
        queued: 0,
        written: 0,
        failed: 0,
        cancelled: 0,
        unknown: 0,
      };
      for (const file of details) {
        if (file.stage === FileRunStage.QueuedForWrite) statusCounts.queued++;
        else if (file.stage === FileRunStage.Written) statusCounts.written++;
        else if (file.stage === FileRunStage.Failed) statusCounts.failed++;
        else if (file.stage === FileRunStage.Cancelled) statusCounts.cancelled++;
        else statusCounts.unknown++;
      }
      return Object.freeze({
        id,
        memberName: first.memberName,
        scriptId: first.parentScriptId,
        scriptCode: first.scriptCode,
        count: details.length,
        totalRows: details.reduce((sum, file) => sum + (file.rows ?? 0), 0),
        totalBytes: details.reduce((sum, file) => sum + (file.bytes ?? 0), 0),
        statusCounts: Object.freeze(statusCounts),
        details,
        failure: details.find((file) => file.failure)?.failure ?? null,
        identity: first.identity,
      });
    })
    .sort((left, right) =>
      compareOrder(
        null,
        minSequence(left.details),
        left.id,
        null,
        minSequence(right.details),
        right.id,
      ),
    );
}

function collectFailures(
  run: RunStatusInfo,
  members: ProcessMemberCard[],
  scripts: ProcessScriptCard[],
  files: ProcessFileCard[],
  batches: ProcessBatchCard[],
  workers: ProcessWorkerSlot[],
): ProcessFailureDetail[] {
  const runFailure = toFailure(run.failure, reference('run', run.correlationId));
  return uniqueBy(
    [
      runFailure,
      ...members.map((item) => item.failure),
      ...scripts.map((item) => item.failure),
      ...files.map((item) => item.failure),
      ...batches.map((item) => item.failure),
      ...workers.map((item) => item.failure),
    ].filter((failure): failure is ProcessFailureDetail => !!failure),
    (failure) =>
      `${failure.reference.type}:${failure.reference.id}:${failure.code}:${failure.occurredAt ?? ''}`,
  );
}

function toFailure(
  failure: RunnerFailureInfo | null | undefined,
  fallback: ProcessEntityReference,
): ProcessFailureDetail | null {
  if (!failure) return null;
  const entityType = normalizeEntityType(failure.entityType) ?? fallback.type;
  return Object.freeze({
    stage: cleanString(failure.stage) || 'unknown',
    code: cleanString(failure.code) || 'unknown',
    message: cleanString(failure.message) || 'Неизвестная ошибка',
    occurredAt: cleanTimestamp(failure.occurredAt),
    reference: Object.freeze({
      type: entityType,
      id: cleanString(failure.entityId) || fallback.id,
      memberName: cleanString(failure.memberName) || fallback.memberName,
      scriptCode: cleanString(failure.scriptCode) || fallback.scriptCode,
      workerId: toNumber(failure.workerId) ?? fallback.workerId,
      batchId: cleanString(failure.batchId) || fallback.batchId,
    }),
    filePath: cleanString(failure.filePath) || null,
    chunkNumber: toNumber(failure.chunkNumber),
  });
}

function reference(
  type: ProcessEntityType,
  id: string,
  memberName: string | null = null,
  scriptCode: string | null = null,
  workerId: number | null = null,
  batchId: string | null = null,
): ProcessEntityReference {
  return { type, id, memberName, scriptCode, workerId, batchId };
}

function normalizeEntityType(value: unknown): ProcessEntityType | null {
  const normalized = cleanString(value).toLowerCase();
  return ['member', 'script', 'file', 'worker', 'batch', 'run'].includes(normalized)
    ? (normalized as ProcessEntityType)
    : null;
}

function sortMembers(items: ProcessMemberCard[]): ProcessMemberCard[] {
  return [...items].sort((left, right) =>
    compareOrder(
      left.queuePosition,
      left.sequence,
      left.name,
      right.queuePosition,
      right.sequence,
      right.name,
    ),
  );
}

function sortScripts(items: ProcessScriptCard[]): ProcessScriptCard[] {
  return [...items].sort((left, right) =>
    compareOrder(
      left.workOrder,
      left.sequence,
      left.scriptCode || left.id,
      right.workOrder,
      right.sequence,
      right.scriptCode || right.id,
    ),
  );
}

function sortFiles(items: ProcessFileCard[]): ProcessFileCard[] {
  return [...items].sort((left, right) =>
    compareOrder(
      left.sequence,
      left.chunk,
      left.fileName || left.path || left.id,
      right.sequence,
      right.chunk,
      right.fileName || right.path || right.id,
    ),
  );
}

function sortBatches(items: ProcessBatchCard[]): ProcessBatchCard[] {
  return [...items].sort((left, right) =>
    compareOrder(null, left.sequence, left.id, null, right.sequence, right.id),
  );
}

function compareOrder(
  leftPrimary: number | null,
  leftSequence: number | null,
  leftFallback: string,
  rightPrimary: number | null,
  rightSequence: number | null,
  rightFallback: string,
): number {
  return (
    compareNullableNumber(leftPrimary, rightPrimary) ||
    compareNullableNumber(leftSequence, rightSequence) ||
    compareText(leftFallback, rightFallback)
  );
}

function compareNullableNumber(left: number | null, right: number | null): number {
  if (left === null && right === null) return 0;
  if (left === null) return 1;
  if (right === null) return -1;
  return left - right;
}

function groupBy<T>(items: Iterable<T>, keyOf: (item: T) => string): Map<string, T[]> {
  const result = new Map<string, T[]>();
  for (const item of items) {
    const key = keyOf(item);
    const bucket = result.get(key) ?? [];
    bucket.push(item);
    result.set(key, bucket);
  }
  return result;
}

function uniqueBy<T>(items: Iterable<T>, keyOf: (item: T) => string): T[] {
  const result = new Map<string, T>();
  for (const item of items) if (!result.has(keyOf(item))) result.set(keyOf(item), item);
  return [...result.values()];
}

function minSequence(items: readonly ProcessFileCard[]): number | null {
  const values = items
    .map((item) => item.sequence)
    .filter((value): value is number => value !== null);
  return values.length ? Math.min(...values) : null;
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

function filePathKey(value: string): string {
  return cleanString(value)
    .replaceAll('\\', '/')
    .replace(/\/{2,}/g, '/')
    .toLowerCase();
}

function basename(value: string): string {
  const normalized = cleanString(value).replaceAll('\\', '/');
  return normalized.split('/').filter(Boolean).at(-1) ?? normalized;
}

function cleanString(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

function cleanTimestamp(value: unknown): string | null {
  const timestamp = cleanString(value);
  return timestamp && toEpoch(timestamp) !== null ? timestamp : null;
}

function toEpoch(value: unknown): number | null {
  if (typeof value !== 'string' || !value) return null;
  const epoch = Date.parse(value);
  return Number.isFinite(epoch) ? epoch : null;
}

function duration(start: string | null, end: string | Date | null): number | null {
  const startEpoch = toEpoch(start);
  const endEpoch = end instanceof Date ? end.getTime() : toEpoch(end);
  return startEpoch === null || endEpoch === null ? null : Math.max(0, endEpoch - startEpoch);
}

function toNumber(value: number | string | null | undefined): number | null {
  if (value === null || value === undefined || (typeof value === 'string' && !value.trim()))
    return null;
  const result = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(result) ? result : null;
}

function normalizeEnum(value: number | string | null | undefined): number | null {
  return toNumber(value);
}
