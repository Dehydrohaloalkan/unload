import { RunStatusInfo } from '../../app.models';
import { resolveGatewayDelivery } from './gateway-history-projection.util';
import { HistoryFileRow, HistoryRunNode } from './history-projection.models';
import { resolveRunStatusLabel } from './labels.util';
import { buildRunMemberIndex, isFileSentViaBatch, memberKey } from './member-index.util';
import { sortNames } from './sort.util';

export function buildRunHistoryNode(
  run: RunStatusInfo,
  knownMemberNames: string[],
  confirmedSentPaths: Set<string>,
): HistoryRunNode | null {
  const correlationId = run.correlationId?.trim();
  if (!correlationId) {
    return null;
  }

  const index = buildRunMemberIndex(run);
  const memberFiles = buildRunMemberFiles(run, correlationId, confirmedSentPaths, index);
  const publishToGateway = run.publishToGateway ?? true;
  return {
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
  };
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
    const row: HistoryFileRow = {
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
    };
    memberMap.set(memberName, [...(memberMap.get(memberName) ?? []), row]);
  }

  return Object.fromEntries(memberMap);
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
