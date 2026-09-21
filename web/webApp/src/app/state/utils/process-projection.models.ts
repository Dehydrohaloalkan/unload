import {
  FileRunStage,
  MemberRunLifecycleStatus,
  ScriptRunStage,
  SenderBatchStatus,
} from '../../app.models';

export interface ProcessFileCard {
  id: string;
  memberName: string;
  parentScriptId: string;
  chunk: number | null;
  stage: FileRunStage | null;
  fileName: string | null;
  path: string | null;
  rows: number | null;
  bytes: number | null;
  queuedAt: string | null;
  stageEnteredAt: string | null;
  updatedAt: string | null;
  completedAt: string | null;
  workerId: number | null;
  writeElapsedMs: number | null;
}

export interface ProcessScriptCard {
  id: string;
  memberName: string;
  scriptCode: string;
  stage: ScriptRunStage | null;
  workerId: number | null;
  records: number | null;
  discoveredAt: string | null;
  stageEnteredAt: string | null;
  startedAt: string | null;
  updatedAt: string | null;
  completedAt: string | null;
  queueWaitMs: number | null;
  stageElapsedMs: number | null;
  files: ProcessFileCard[];
}

export interface ProcessBatchCard {
  id: string;
  memberName: string;
  status: SenderBatchStatus | null;
  queuedAt: string | null;
  startedAt: string | null;
  updatedAt: string | null;
  fileCount: number | null;
  sentCount: number;
  queueWaitMs: number | null;
  sendElapsedMs: number | null;
}

export interface ProcessActiveItemSummary {
  count: number;
  scriptCount: number;
  fileCount: number;
  batchCount: number;
  oldestAt: string | null;
  oldestElapsedMs: number | null;
}

export interface ProcessMemberRow {
  key: string;
  name: string;
  status: MemberRunLifecycleStatus | null;
  updatedAt: string | null;
  scripts: ProcessScriptCard[];
  orphanFiles: ProcessFileCard[];
  batches: ProcessBatchCard[];
  activeItemSummary: ProcessActiveItemSummary;
  oldestActiveAt: string | null;
}
