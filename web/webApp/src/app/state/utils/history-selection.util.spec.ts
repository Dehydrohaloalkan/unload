import { HistoryFileRow, HistoryRunNode, HistoryScriptNode } from './history-projection.models';
import {
  collectHistoryFiles,
  collectRunFiles,
  collectScriptFiles,
  resolveHistorySelectionState,
  toggleHistoryFiles,
} from './history-selection.util';

describe('history bulk selection', () => {
  const first = createFile('first');
  const second = createFile('second');
  const third = createFile('third');

  it('collects unique files across run, member, script and bank levels', () => {
    const script = createScript([second, third]);
    const node = createNode([first, second], [script]);

    expect(collectScriptFiles(script).map((file) => file.key)).toEqual(['second', 'third']);
    expect(collectRunFiles(node).map((file) => file.key)).toEqual(['first', 'second', 'third']);
    expect(collectHistoryFiles([node]).map((file) => file.key)).toEqual([
      'first',
      'second',
      'third',
    ]);
  });

  it('reports checked and indeterminate group states', () => {
    expect(resolveHistorySelectionState([first, second], [])).toEqual({
      checked: false,
      indeterminate: false,
      selected: 0,
      total: 2,
    });
    expect(resolveHistorySelectionState([first, second], [first])).toEqual({
      checked: false,
      indeterminate: true,
      selected: 1,
      total: 2,
    });
    expect(resolveHistorySelectionState([first, second], [first, second])).toEqual({
      checked: true,
      indeterminate: false,
      selected: 2,
      total: 2,
    });
  });

  it('selects and clears a group without losing files selected elsewhere', () => {
    const selected = toggleHistoryFiles([third], [first, second], true);
    expect(selected.map((file) => file.key)).toEqual(['third', 'first', 'second']);

    expect(toggleHistoryFiles(selected, [first, second], false).map((file) => file.key)).toEqual([
      'third',
    ]);
  });
});

function createFile(key: string): HistoryFileRow {
  return {
    key,
    taskCode: 'run',
    correlationId: 'run-1',
    memberName: 'Member A',
    fileName: `${key}.csv`,
    filePath: `${key}.csv`,
    occurredAt: '2026-08-14T10:00:00Z',
    sentToGateway: false,
    queuedForGateway: false,
    gatewayDeliveries: [],
  };
}

function createScript(files: HistoryFileRow[]): HistoryScriptNode {
  return {
    scriptCode: 'SCRIPT_A',
    banks: [{ bankName: 'Bank A', files }],
    fileCount: files.length,
  };
}

function createNode(mainFiles: HistoryFileRow[], scripts: HistoryScriptNode[]): HistoryRunNode {
  return {
    key: 'run|run-1',
    taskCode: 'run',
    correlationId: 'run-1',
    status: 'Завершено',
    startedAt: '2026-08-14T10:00:00Z',
    completedAt: '2026-08-14T10:05:00Z',
    occurredAt: '2026-08-14T10:05:00Z',
    publishToGateway: true,
    gatewayDelivery: 'delivered',
    memberNames: ['Member A'],
    memberFiles: { 'Member A': mainFiles },
    gatewayAttempts: [],
    scripts,
  };
}
