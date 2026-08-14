import { OutputFileInfo, RunStatusInfo, SenderBatchStatus, TaskRecord } from '../../app.models';

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
  queuedForGateway: boolean;
  gatewayDeliveries: HistoryFileDelivery[];
  scriptCode?: string;
  bankName?: string;
}

export interface HistoryFileDelivery {
  batchId: string;
  sentAt: string;
}

export interface HistoryGatewayAttempt {
  batchId: string;
  memberName: string;
  status: SenderBatchStatus;
  updatedAt: string;
  sentFileCount: number;
  message: string | null;
  repeated: boolean;
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
  gatewayAttempts: HistoryGatewayAttempt[];
  scripts?: HistoryScriptNode[];
}

export interface HistoryProjectionInput {
  todayRuns: RunStatusInfo[];
  todayHistory: TaskRecord[];
  allTodayRuns: RunStatusInfo[];
  outputFilesByPath: Record<string, OutputFileInfo[]>;
  knownMemberNames: string[];
  queuedGatewayPaths: Set<string>;
  bankNamesByCode?: Record<string, string>;
}

export interface RequeueFileSummary {
  total: number;
  accepted: number;
  rejected: number;
}
