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
  unsentCount: number;
  /** True for snapshots written before plannedFiles became part of the contract. */
  hasLegacyUnsentAmbiguity: boolean;
  senderFiles: readonly ProcessDispatchFile[];
  sentFiles: readonly ProcessDispatchFile[];
  totalElapsedMs: number | null;
  failure: ProcessFailureDetail | null;
  identity: ProcessMemberIdentity;
}

export interface ProcessDispatchFile {
  id: string;
  batchId: string;
  memberName: string;
  fileName: string;
  path: string;
  queuedAt: string | null;
  sentAt: string | null;
  estimatedBytes: number | null;
  actualBytes: number | null;
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
  details: readonly ProcessFileCard[];
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
  /** One globally ordered list of batches that still require delivery confirmation. */
  senderBatches: readonly ProcessBatchCard[];
  senderQueue: readonly ProcessBatchCard[];
  senderInProgress: readonly ProcessBatchCard[];
  delivered: readonly ProcessBatchCard[];
  failures: readonly ProcessFailureDetail[];
}
