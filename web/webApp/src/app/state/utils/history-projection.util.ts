import {
  OutputFileInfo,
  RequeueToGatewayResponse,
  RunStatusInfo,
  SenderBatchStatus,
  SenderBatchStatusInfo,
  TaskRecord,
} from '../../app.models';
import { byDescDate } from './compare.util';
import { resolveRunStatusLabel } from './labels.util';
import {
  buildRunMemberIndex,
  extraFilePathKey,
  isExtraFileSentViaBatch,
  isFileSentViaBatch,
  memberKey,
} from './member-index.util';
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
  // Заполняются для extra: код скрипта (он же MemberName партии шлюза) и читаемый банк.
  scriptCode?: string;
  bankName?: string;
}

export interface HistoryBankNode {
  bankName: string;
  files: HistoryFileRow[];
}

export interface HistoryScriptNode {
  scriptCode: string;
  banks: HistoryBankNode[];
  fileCount: number;
}

/**
 * Фактический результат доставки в шлюз. «Жёлтый/красный» — только при реальном сбое отправки,
 * а не когда какой-то скрипт дал 0 файлов или пофайловое подтверждение пришло не полностью.
 * - `off` — выгрузка без шлюза (publishToGateway=false);
 * - `delivered` — отправлять было нечего или всё, что встало в очередь, ушло без сбоя;
 * - `partial` — была ошибка отправки, но часть партий всё же доставлена;
 * - `notSent` — ничего не доставлено (есть файлы, ни один не отправлен) без явного сбоя;
 * - `failed` — отправка дала сбой и не доставлено ничего.
 */
export type GatewayDelivery = 'off' | 'delivered' | 'partial' | 'notSent' | 'failed';

export interface HistoryRunNode {
  key: string;
  taskCode: HistoryTaskCode;
  correlationId: string;
  status: string;
  startedAt: string;
  completedAt: string;
  occurredAt: string;
  publishToGateway: boolean;
  gatewayDelivery: GatewayDelivery;
  memberNames: string[];
  memberFiles: Record<string, HistoryFileRow[]>;
  // Для extra — 3-уровневая структура: скрипт → банк → файлы.
  scripts?: HistoryScriptNode[];
}

export interface HistoryProjectionInput {
  todayRuns: RunStatusInfo[];
  todayHistory: TaskRecord[];
  allTodayRuns: RunStatusInfo[];
  outputFilesByPath: Record<string, OutputFileInfo[]>;
  knownMemberNames: string[];
  confirmedSentPaths: Set<string>;
  // Код банка (NrBank) → читаемое название. Для дерева extra-истории показываем название, а не код.
  bankNamesByCode?: Record<string, string>;
}

export function buildHistoryNodes(input: HistoryProjectionInput): HistoryRunNode[] {
  const nodeMap = new Map<string, HistoryRunNode>();
  const {
    todayRuns,
    todayHistory,
    allTodayRuns,
    outputFilesByPath,
    knownMemberNames,
    confirmedSentPaths,
    bankNamesByCode = {},
  } = input;

  for (const run of todayRuns) {
    const correlationId = run.correlationId?.trim();
    if (!correlationId) {
      continue;
    }

    const index = buildRunMemberIndex(run);
    const memberFiles = buildRunMemberFiles(run, correlationId, confirmedSentPaths, index);
    const publishToGateway = run.publishToGateway ?? true;
    nodeMap.set(`run|${correlationId}`, {
      key: `run|${correlationId}`,
      taskCode: 'run',
      correlationId,
      status: resolveRunStatusLabel(run.status),
      startedAt: run.createdAt,
      completedAt: run.updatedAt,
      occurredAt: run.updatedAt,
      publishToGateway,
      gatewayDelivery: resolveGatewayDelivery(
        publishToGateway,
        Object.values(memberFiles).flat(),
        Array.from(index.batches.values()),
      ),
      memberNames: collectRunMemberNames(run, knownMemberNames),
      memberFiles,
    });
  }

  // extra-узлы строим из ВСЕХ сегодняшних extra-запусков (любой статус), как main из todayRuns,
  // — иначе завершённые/упавшие/отменённые выгрузки не попадали в Историю (todayHistory хранит
  // только Completed) и после ухода из активного вида «пропадали» в никуда.
  for (const run of allTodayRuns) {
    const correlationId = (run.correlationId ?? '').trim();
    if (!correlationId || (run.taskCode ?? '').trim().toLowerCase() !== 'extra') {
      continue;
    }

    const extraIndex = buildRunMemberIndex(run);
    // Запись из todayHistory (если есть) даёт точные времена старта/завершения и outputPath.
    const record = todayHistory.find(
      (item) => item.taskCode === 'extra' && item.correlationId === correlationId,
    );

    // Источник файлов: диск-скан по outputPath (полный список для завершённых) ∪ артефакты событий
    // (покрывают активные/упавшие выгрузки, для которых диск ещё не сканировался).
    // Диск-скан отдаёт пути относительно output-корня, артефакты — пути writer'а, поэтому
    // дедупликация идёт по каноническому хвосту пути, а не по сырой строке.
    const outputPath = run.outputPath ?? record?.outputPath ?? null;
    const fileEntries = new Map<string, { filePath: string; fileName: string; occurredAt: string }>();
    if (outputPath) {
      for (const file of outputFilesByPath[outputPath] ?? []) {
        fileEntries.set(extraFilePathKey(file.filePath), {
          filePath: file.filePath,
          fileName: file.fileName,
          occurredAt: file.modifiedAt || run.updatedAt,
        });
      }
    }
    for (const artifact of run.outputArtifacts ?? []) {
      if (artifact.filePath && artifact.fileName && !fileEntries.has(extraFilePathKey(artifact.filePath))) {
        fileEntries.set(extraFilePathKey(artifact.filePath), {
          filePath: artifact.filePath,
          fileName: artifact.fileName,
          occurredAt: artifact.occurredAt || run.updatedAt,
        });
      }
    }

    // Группировка extra-файлов по скрипту и банку: путь вида
    // .../output-files/<scriptCode>/<bank>/<file>. Партия шлюза — на скрипт (MemberName=scriptCode).
    // Группируем по коду банка (NrBank из пути), но показываем читаемое название.
    const scriptMap = new Map<string, Map<string, HistoryFileRow[]>>();
    for (const [pathKey, meta] of fileEntries) {
      const { scriptCode, bankCode } = parseExtraFilePath(meta.filePath, meta.fileName);
      const bankName = bankNamesByCode[bankCode] ?? bankNamesByCode[bankCode.toUpperCase()] ?? bankCode;
      const batch = extraIndex.batches.get(memberKey(scriptCode));
      const bankMap = scriptMap.get(scriptCode) ?? new Map<string, HistoryFileRow[]>();
      const bucket = bankMap.get(bankCode) ?? [];
      bucket.push({
        key: `extra|${correlationId}|${pathKey}`,
        taskCode: 'extra',
        correlationId,
        memberName: scriptCode,
        scriptCode,
        bankName,
        fileName: meta.fileName,
        filePath: meta.filePath,
        occurredAt: meta.occurredAt,
        sentToGateway:
          confirmedSentPaths.has(meta.filePath) ||
          isExtraFileSentViaBatch(meta.filePath, batch?.sentFiles ?? null),
      });
      bankMap.set(bankCode, bucket);
      scriptMap.set(scriptCode, bankMap);
    }

    const scripts: HistoryScriptNode[] = Array.from(scriptMap.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([scriptCode, bankMap]) => {
        const banks: HistoryBankNode[] = Array.from(bankMap.entries())
          // Сортируем по отображаемому названию банка.
          .map(([bankCode, bankFiles]) => ({
            bankName: bankFiles[0]?.bankName ?? bankCode,
            files: bankFiles,
          }))
          .sort((a, b) => a.bankName.localeCompare(b.bankName));
        return {
          scriptCode,
          banks,
          fileCount: banks.reduce((sum, bank) => sum + bank.files.length, 0),
        };
      });

    const extraPublishToGateway = run.publishToGateway ?? false;
    nodeMap.set(`extra|${correlationId}`, {
      key: `extra|${correlationId}`,
      taskCode: 'extra',
      correlationId,
      status: resolveRunStatusLabel(run.status),
      startedAt: record?.startedAt ?? run.createdAt,
      completedAt: record?.completedAt ?? run.updatedAt,
      occurredAt: run.updatedAt,
      publishToGateway: extraPublishToGateway,
      gatewayDelivery: resolveGatewayDelivery(
        extraPublishToGateway,
        scripts.flatMap((script) => script.banks.flatMap((bank) => bank.files)),
        Array.from(extraIndex.batches.values()),
      ),
      memberNames: sortNames(knownMemberNames),
      memberFiles: {},
      scripts,
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

/**
 * Вычисляет фактический результат доставки.
 *
 * Жёлтый «частично»/красный «ошибка» показываем ТОЛЬКО при настоящем сбое отправки
 * (партия шлюза в статусе Failed). Если сбоев нет — выгрузка считается доставленной,
 * даже если какой-то скрипт дал 0 файлов или пофайловое подтверждение пришло не полностью
 * (статус успешной партии авторитетнее, чем список подтверждённых файлов).
 */
function resolveGatewayDelivery(
  publishToGateway: boolean,
  files: HistoryFileRow[],
  batches: SenderBatchStatusInfo[],
): GatewayDelivery {
  if (!publishToGateway) {
    return 'off';
  }

  const sent = files.filter((file) => file.sentToGateway).length;
  const hasFailure = batches.some((batch) => batch.status === SenderBatchStatus.Failed);

  // Реального сбоя нет: отправлять было нечего либо всё ушло — это успех, не тревога.
  if (!hasFailure) {
    return 'delivered';
  }

  // Есть сбой отправки: «частично», если что-то всё же доставлено, иначе «ошибка».
  return sent > 0 ? 'partial' : 'failed';
}

function buildRunMemberFiles(
  run: RunStatusInfo,
  correlationId: string,
  confirmedSentPaths: Set<string>,
  index: ReturnType<typeof buildRunMemberIndex>,
): Record<string, HistoryFileRow[]> {
  const memberMap = new Map<string, HistoryFileRow[]>();

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

/**
 * Извлекает код скрипта и код банка (NrBank) из относительного пути extra-файла
 * (`.../output-files/<scriptCode>/<bankCode>/<file>`). Поддерживает оба разделителя путей.
 */
function parseExtraFilePath(
  filePath: string,
  fileName: string,
): { scriptCode: string; bankCode: string } {
  const segments = (filePath ?? '').split(/[\\/]/).filter((segment) => segment.length > 0);
  const anchor = segments.lastIndexOf('output-files');
  if (anchor >= 0 && segments.length >= anchor + 3) {
    return { scriptCode: segments[anchor + 1], bankCode: segments[anchor + 2] };
  }

  // Фолбэк для неожиданной структуры: имя файла без расширения как код скрипта.
  const dotIndex = fileName.lastIndexOf('.');
  const fallback = dotIndex > 0 ? fileName.slice(0, dotIndex) : fileName || 'UNKNOWN';
  return { scriptCode: fallback, bankCode: 'UNKNOWN' };
}
