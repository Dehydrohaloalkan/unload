import { OutputFileInfo, RunStatusInfo, TaskRecord } from '../../app.models';
import {
  buildFileGatewayDeliveries,
  buildGatewayAttempts,
  resolveGatewayDelivery,
} from './gateway-history-projection.util';
import {
  HistoryBankNode,
  HistoryFileRow,
  HistoryRunNode,
  HistoryScriptNode,
} from './history-projection.models';
import { buildRunMemberIndex, extraFilePathKey, memberKey } from './member-index.util';
import { resolveRunStatusLabel } from './labels.util';
import { sortNames } from './sort.util';

export function buildExtraHistoryNode(
  run: RunStatusInfo,
  todayHistory: TaskRecord[],
  outputFilesByPath: Record<string, OutputFileInfo[]>,
  knownMemberNames: string[],
  queuedGatewayPaths: Set<string>,
  bankNamesByCode: Record<string, string>,
): HistoryRunNode | null {
  const correlationId = (run.correlationId ?? '').trim();
  if (!correlationId || (run.taskCode ?? '').trim().toLowerCase() !== 'extra') {
    return null;
  }

  const record = todayHistory.find(
    (item) => item.taskCode === 'extra' && item.correlationId === correlationId,
  );
  const index = buildRunMemberIndex(run);
  const files = collectExtraFiles(run, record, outputFilesByPath);
  const scripts = buildExtraScripts(
    files,
    run,
    correlationId,
    index,
    queuedGatewayPaths,
    bankNamesByCode,
  );
  const publishToGateway = run.publishToGateway ?? false;

  return {
    key: `extra|${correlationId}`,
    taskCode: 'extra',
    correlationId,
    status: resolveRunStatusLabel(run.status),
    startedAt: record?.startedAt ?? run.createdAt,
    completedAt: record?.completedAt ?? run.updatedAt,
    occurredAt: run.updatedAt,
    publishToGateway,
    gatewayDelivery: resolveGatewayDelivery(
      publishToGateway,
      scripts.flatMap((script) => script.banks.flatMap((bank) => bank.files)),
      Object.values(run.senderBatches ?? {}),
    ),
    memberNames: sortNames(knownMemberNames),
    memberFiles: {},
    gatewayAttempts: buildGatewayAttempts(run),
    scripts,
  };
}

type ExtraFile = { filePath: string; fileName: string; occurredAt: string };

function collectExtraFiles(
  run: RunStatusInfo,
  record: TaskRecord | undefined,
  outputFilesByPath: Record<string, OutputFileInfo[]>,
): Map<string, ExtraFile> {
  const entries = new Map<string, ExtraFile>();
  const outputPath = run.outputPath ?? record?.outputPath ?? null;
  if (outputPath) {
    for (const file of outputFilesByPath[outputPath] ?? []) {
      entries.set(extraFilePathKey(file.filePath), {
        filePath: file.filePath,
        fileName: file.fileName,
        occurredAt: file.modifiedAt || run.updatedAt,
      });
    }
  }

  for (const artifact of run.outputArtifacts ?? []) {
    if (artifact.filePath && artifact.fileName && !entries.has(extraFilePathKey(artifact.filePath))) {
      entries.set(extraFilePathKey(artifact.filePath), {
        filePath: artifact.filePath,
        fileName: artifact.fileName,
        occurredAt: artifact.occurredAt || run.updatedAt,
      });
    }
  }
  return entries;
}

function buildExtraScripts(
  files: Map<string, ExtraFile>,
  run: RunStatusInfo,
  correlationId: string,
  index: ReturnType<typeof buildRunMemberIndex>,
  queuedGatewayPaths: Set<string>,
  bankNamesByCode: Record<string, string>,
): HistoryScriptNode[] {
  const scriptMap = new Map<string, Map<string, HistoryFileRow[]>>();
  for (const [pathKey, file] of files) {
    const { scriptCode, bankCode } = parseExtraFilePath(file.filePath, file.fileName);
    const bankName = bankNamesByCode[bankCode] ?? bankNamesByCode[bankCode.toUpperCase()] ?? bankCode;
    const batches = index.batchGroups.get(memberKey(scriptCode)) ?? [];
    const gatewayDeliveries = buildFileGatewayDeliveries(file.filePath, batches, true);
    const bankMap = scriptMap.get(scriptCode) ?? new Map<string, HistoryFileRow[]>();
    const row: HistoryFileRow = {
      key: `extra|${correlationId}|${pathKey}`,
      taskCode: 'extra',
      correlationId,
      memberName: scriptCode,
      scriptCode,
      bankName,
      fileName: file.fileName,
      filePath: file.filePath,
      occurredAt: file.occurredAt,
      sentToGateway: gatewayDeliveries.length > 0,
      queuedForGateway: queuedGatewayPaths.has(file.filePath) && gatewayDeliveries.length === 0,
      gatewayDeliveries,
    };
    bankMap.set(bankCode, [...(bankMap.get(bankCode) ?? []), row]);
    scriptMap.set(scriptCode, bankMap);
  }

  return Array.from(scriptMap.entries())
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([scriptCode, bankMap]) => {
      const banks: HistoryBankNode[] = Array.from(bankMap.entries())
        .map(([bankCode, bankFiles]) => ({
          bankName: bankFiles[0]?.bankName ?? bankCode,
          files: bankFiles,
        }))
        .sort((left, right) => left.bankName.localeCompare(right.bankName));
      return {
        scriptCode,
        banks,
        fileCount: banks.reduce((sum, bank) => sum + bank.files.length, 0),
      };
    });
}

function parseExtraFilePath(
  filePath: string,
  fileName: string,
): { scriptCode: string; bankCode: string } {
  const segments = (filePath ?? '').split(/[\\/]/).filter((segment) => segment.length > 0);
  const anchor = segments.lastIndexOf('output-files');
  if (anchor >= 0 && segments.length >= anchor + 3) {
    return { scriptCode: segments[anchor + 1], bankCode: segments[anchor + 2] };
  }

  const dotIndex = fileName.lastIndexOf('.');
  const fallback = dotIndex > 0 ? fileName.slice(0, dotIndex) : fileName || 'UNKNOWN';
  return { scriptCode: fallback, bankCode: 'UNKNOWN' };
}
