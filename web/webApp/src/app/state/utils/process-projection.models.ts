import {
  FileRunStage,
  MemberRunLifecycleStatus,
  ScriptRunStage,
  SenderBatchStatus,
} from '../../app.models';

export type ProcessEntityType = 'member' | 'script' | 'file' | 'worker' | 'batch' | 'run';
export type ProcessMemberZone = 'input' | 'resolver' | 'completed';
export type ProcessScriptZone = 'script-queue' | 'worker' | 'completed';
export type ProcessBatchZone = 'sender-queue' | 'sender-in-progress' | 'delivered';

export interface ProcessEntityReference {
  type: ProcessEntityType;
  id: string;
  memberName: string | null;
  scriptCode: string | null;
  workerId: number | null;
  batchId: string | null;
}

/** Safe, click-ready failure data. Server messages are rendered as text, never as markup. */
export interface ProcessFailureDetail {
  stage: string;
  code: string;
  message: string;
  occurredAt: string | null;
  reference: ProcessEntityReference;
  filePath: string | null;
  chunkNumber: number | null;
}

export interface ProcessMemberIdentity {
  index: number;
  token: `member-${number}`;
  className: `process-member-accent--${number}`;
}

export interface ProcessMemberCard {
  key: string;
  name: string;
  status: MemberRunLifecycleStatus | null;
  zone: ProcessMemberZone;
  queuePosition: number | null;
  sequence: number | null;
  updatedAt: string | null;
  failure: ProcessFailureDetail | null;
  identity: ProcessMemberIdentity;
}

export interface ProcessFileCard {
  id: string;
  memberName: string;
  scriptCode: string;
  parentScriptId: string;
  chunk: number | null;
  sequence: number | null;
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
  failure: ProcessFailureDetail | null;
  identity: ProcessMemberIdentity;
}

export interface ProcessScriptCard {
  id: string;
  memberName: string;
  scriptCode: string;
  stage: ScriptRunStage | null;
  zone: ProcessScriptZone;
  workOrder: number | null;
  sequence: number | null;
  workerId: number | null;
  records: number | null;
  discoveredAt: string | null;
  stageEnteredAt: string | null;
  startedAt: string | null;
  updatedAt: string | null;
  completedAt: string | null;
  queueWaitMs: number | null;
  stageElapsedMs: number | null;
  files: readonly ProcessFileCard[];
  failure: ProcessFailureDetail | null;
  identity: ProcessMemberIdentity;
}

export interface ProcessBatchCard {
  id: string;
  memberName: string;
  status: SenderBatchStatus | null;
  zone: ProcessBatchZone;
  sequence: number | null;
  queuedAt: string | null;
  startedAt: string | null;
  updatedAt: string | null;
  fileCount: number | null;
  sentCount: number;
  queueWaitMs: number | null;
  sendElapsedMs: number | null;
  failure: ProcessFailureDetail | null;
  identity: ProcessMemberIdentity;
}

export interface ProcessFileStatusCounts {
  queued: number;
  written: number;
  failed: number;
  cancelled: number;
  unknown: number;
}

export interface ProcessFileGroup {
  id: string;
  memberName: string;
  scriptId: string;
  scriptCode: string;
  count: number;
  totalRows: number;
  totalBytes: number;
  statusCounts: ProcessFileStatusCounts;
  /** Bounded initial detail window. Use getProcessFileGroupPage for subsequent chunks. */
  initialDetails: readonly ProcessFileCard[];
  details: readonly ProcessFileCard[];
  detailPageSize: number;
  failure: ProcessFailureDetail | null;
  identity: ProcessMemberIdentity;
}

export interface ProcessWorkerSlot {
  workerId: number;
  state: string;
  assignment: ProcessScriptCard | null;
  retainedFailures: readonly ProcessScriptCard[];
  sequence: number | null;
  failure: ProcessFailureDetail | null;
}

export interface ProcessPipelineViewModel {
  correlationId: string;
  members: readonly ProcessMemberCard[];
  scripts: readonly ProcessScriptCard[];
  memberInput: readonly ProcessMemberCard[];
  resolver: readonly ProcessMemberCard[];
  scriptQueue: readonly ProcessScriptCard[];
  workers: readonly ProcessWorkerSlot[];
  fileGroups: readonly ProcessFileGroup[];
  senderQueue: readonly ProcessBatchCard[];
  senderInProgress: readonly ProcessBatchCard[];
  delivered: readonly ProcessBatchCard[];
  completedMembers: readonly ProcessMemberCard[];
  completedScripts: readonly ProcessScriptCard[];
  failures: readonly ProcessFailureDetail[];
}

export interface ProcessActiveItemSummary {
  count: number;
  scriptCount: number;
  fileCount: number;
  batchCount: number;
  oldestAt: string | null;
  oldestElapsedMs: number | null;
}

/** Compatibility model for the existing four-column template until its vertical replacement lands. */
export interface ProcessMemberRow {
  key: string;
  name: string;
  status: MemberRunLifecycleStatus | null;
  memberFailure: ProcessFailureDetail | null;
  updatedAt: string | null;
  scripts: ProcessScriptCard[];
  orphanFiles: ProcessFileCard[];
  files: ReadonlyArray<{ file: ProcessFileCard; parentScriptCode: string | null }>;
  fileTotalCount: number;
  fileStatusCounts: ProcessFileStatusCounts;
  fileFailureCount: number;
  fileFailure: ProcessFailureDetail | null;
  batches: ProcessBatchCard[];
  activeItemSummary: ProcessActiveItemSummary;
  oldestActiveAt: string | null;
  identity: ProcessMemberIdentity;
}
