import { RunStatusInfo, SenderBatchStatusInfo } from '../../app.models';

/** Нормализованный ключ из имени мембера (lowercase + trim). */
export function memberKey(value: string | null | undefined): string {
  return (value ?? '').trim().toLowerCase();
}

/** Нормализованный путь файла для сравнения (lowercase, разделители унифицированы). */
export function normalizeFilePath(value: string): string {
  return value.trim().toLowerCase().replaceAll('\\', '/');
}

/** Группирует все попытки gateway-отправки по мемберу, включая повторные requeue-партии. */
export function buildMemberBatchGroups(
  run: RunStatusInfo | null,
): Map<string, SenderBatchStatusInfo[]> {
  const batchGroups = new Map<string, SenderBatchStatusInfo[]>();

  if (!run) {
    return batchGroups;
  }

  for (const batch of Object.values(run.senderBatches ?? {})) {
    const key = memberKey(batch.memberName);
    if (!key) continue;
    batchGroups.set(key, [...(batchGroups.get(key) ?? []), batch]);
  }

  for (const group of batchGroups.values()) {
    group.sort((left, right) => Date.parse(right.updatedAt) - Date.parse(left.updatedAt));
  }

  return batchGroups;
}

/**
 * Канонический ключ extra-файла: хвост пути от `output-files/`.
 * Один и тот же файл приходит в разном виде — абсолютным путём из артефактов run'а
 * и путём относительно output-корня из диск-скана; общий у них только этот хвост.
 */
export function extraFilePathKey(filePath: string): string {
  const normalized = normalizeFilePath(filePath);
  const anchor = normalized.lastIndexOf('output-files/');
  return anchor >= 0 ? normalized.slice(anchor) : normalized;
}
