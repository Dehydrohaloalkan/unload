import {
  MemberRunStatusInfo,
  RunOutputArtifactInfo,
  RunStatusInfo,
  RunWorkerStatusInfo,
  SenderBatchStatusInfo,
  SenderFileDispatchStateInfo,
} from '../../app.models';

/** Нормализованный ключ из имени мембера (lowercase + trim). */
export function memberKey(value: string | null | undefined): string {
  return (value ?? '').trim().toLowerCase();
}

/** Нормализованный путь файла для сравнения (lowercase, разделители унифицированы). */
export function normalizeFilePath(value: string): string {
  return value.trim().toLowerCase().replaceAll('\\', '/');
}

export interface RunMemberIndex {
  statuses: Map<string, MemberRunStatusInfo>;
  batches: Map<string, SenderBatchStatusInfo>;
  workers: Map<string, RunWorkerStatusInfo[]>;
  artifacts: Map<string, RunOutputArtifactInfo[]>;
  /** Уникальные member-имена, замеченные в любом из подразделов run'а. */
  memberNames: Set<string>;
}

/** Один проход по run-у — строим O(1) индексы вместо `Object.values(...).find(...)` в шаблонах. */
export function buildRunMemberIndex(run: RunStatusInfo | null): RunMemberIndex {
  const statuses = new Map<string, MemberRunStatusInfo>();
  const batches = new Map<string, SenderBatchStatusInfo>();
  const workers = new Map<string, RunWorkerStatusInfo[]>();
  const artifacts = new Map<string, RunOutputArtifactInfo[]>();
  const memberNames = new Set<string>();

  if (!run) {
    return { statuses, batches, workers, artifacts, memberNames };
  }

  for (const status of Object.values(run.memberStatuses ?? {})) {
    const key = memberKey(status.memberName);
    if (!key) continue;
    statuses.set(key, status);
    memberNames.add(status.memberName);
  }

  for (const batch of Object.values(run.senderBatches ?? {})) {
    const key = memberKey(batch.memberName);
    if (!key) continue;
    batches.set(key, batch);
    memberNames.add(batch.memberName);
  }

  for (const worker of Object.values(run.workerStatuses ?? {})) {
    const key = memberKey(worker.memberName);
    if (!key) continue;
    const bucket = workers.get(key) ?? [];
    bucket.push(worker);
    workers.set(key, bucket);
  }
  for (const bucket of workers.values()) {
    bucket.sort((left, right) => left.workerId - right.workerId);
  }

  for (const artifact of run.outputArtifacts ?? []) {
    if (!artifact.memberName) continue;
    const key = memberKey(artifact.memberName);
    const bucket = artifacts.get(key) ?? [];
    bucket.push(artifact);
    artifacts.set(key, bucket);
    memberNames.add(artifact.memberName);
  }

  return { statuses, batches, workers, artifacts, memberNames };
}

export function isFileSentViaBatch(
  filePath: string,
  sentFiles: SenderFileDispatchStateInfo[] | null | undefined,
): boolean {
  if (!sentFiles?.length) {
    return false;
  }
  const target = normalizeFilePath(filePath);
  return sentFiles.some((file) => normalizeFilePath(file.filePath) === target);
}
