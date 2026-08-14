import {
  HistoryFileRow,
  HistoryRunNode,
  HistoryScriptNode,
} from './history-projection.models';

export interface HistorySelectionState {
  checked: boolean;
  indeterminate: boolean;
  selected: number;
  total: number;
}

export function collectHistoryFiles(nodes: HistoryRunNode[]): HistoryFileRow[] {
  return uniqueFiles(nodes.flatMap(collectRunFiles));
}

export function collectRunFiles(node: HistoryRunNode): HistoryFileRow[] {
  const mainFiles = Object.values(node.memberFiles).flat();
  const extraFiles = (node.scripts ?? []).flatMap(collectScriptFiles);
  return uniqueFiles([...mainFiles, ...extraFiles]);
}

export function collectScriptFiles(script: HistoryScriptNode): HistoryFileRow[] {
  return uniqueFiles(script.banks.flatMap((bank) => bank.files));
}

export function resolveHistorySelectionState(
  files: HistoryFileRow[],
  selectedFiles: HistoryFileRow[],
): HistorySelectionState {
  const unique = uniqueFiles(files);
  const selectedKeys = new Set(selectedFiles.map((file) => file.key));
  const selected = unique.filter((file) => selectedKeys.has(file.key)).length;
  const total = unique.length;
  return {
    checked: total > 0 && selected === total,
    indeterminate: selected > 0 && selected < total,
    selected,
    total,
  };
}

export function toggleHistoryFiles(
  selectedFiles: HistoryFileRow[],
  files: HistoryFileRow[],
  checked: boolean,
): HistoryFileRow[] {
  const next = new Map(selectedFiles.map((file) => [file.key, file]));
  for (const file of files) {
    if (checked) {
      next.set(file.key, file);
    } else {
      next.delete(file.key);
    }
  }
  return Array.from(next.values());
}

function uniqueFiles(files: HistoryFileRow[]): HistoryFileRow[] {
  return Array.from(new Map(files.map((file) => [file.key, file])).values());
}
