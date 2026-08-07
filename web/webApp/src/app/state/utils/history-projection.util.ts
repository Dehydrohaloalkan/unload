import { byDescDate } from './compare.util';
import { buildExtraHistoryNode } from './extra-history-projection.util';
import { HistoryProjectionInput, HistoryRunNode } from './history-projection.models';
import { buildRunHistoryNode } from './run-history-projection.util';

export * from './history-projection.models';
export { buildConfirmedSentPaths, summarizeRequeue } from './gateway-history-projection.util';

/**
 * Собирает единое представление истории из независимых main и extra проекций.
 * Этот файл остаётся стабильной точкой импорта для UI-компонентов.
 */
export function buildHistoryNodes(input: HistoryProjectionInput): HistoryRunNode[] {
  const nodeMap = new Map<string, HistoryRunNode>();
  for (const run of input.todayRuns) {
    const node = buildRunHistoryNode(run, input.knownMemberNames, input.confirmedSentPaths);
    if (node) {
      nodeMap.set(node.key, node);
    }
  }

  const bankNamesByCode = input.bankNamesByCode ?? {};
  for (const run of input.allTodayRuns) {
    const node = buildExtraHistoryNode(
      run,
      input.todayHistory,
      input.outputFilesByPath,
      input.knownMemberNames,
      input.confirmedSentPaths,
      bankNamesByCode,
    );
    if (node) {
      nodeMap.set(node.key, node);
    }
  }

  return Array.from(nodeMap.values()).sort(
    byDescDate<HistoryRunNode>((node) => node.occurredAt),
  );
}
