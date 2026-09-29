import type * as ApiModels from './generated/api/models';

export type RunLifecycleStatus = ApiModels.RunLifecycleStatus;
export const RunLifecycleStatus = {
  Running: 0,
  Completed: 1,
  Failed: 2,
  Cancelled: 3,
  CancellationRequested: 4,
} as const satisfies Record<string, RunLifecycleStatus>;

export type MemberRunLifecycleStatus = ApiModels.MemberRunLifecycleStatus;
export const MemberRunLifecycleStatus = {
  Pending: 0,
  Running: 1,
  Completed: 2,
  Failed: 3,
  Cancelled: 4,
} as const satisfies Record<string, MemberRunLifecycleStatus>;

export type MemberRunStatusInfo = ApiModels.MemberRunStatusInfo;
export type RunOutputArtifactInfo = ApiModels.RunOutputArtifactInfo;
export type SenderBatchStatus = ApiModels.SenderBatchStatus;
export const SenderBatchStatus = {
  Ready: 0,
  InProgress: 1,
  Completed: 2,
  Failed: 3,
  SkippedByRequest: 4,
} as const satisfies Record<string, SenderBatchStatus>;

export type SenderFileDispatchStateInfo = ApiModels.SenderFileDispatchStateInfo;
export type SenderBatchStatusInfo = ApiModels.SenderBatchStatusInfo;
export type RunnerFailureInfo = ApiModels.RunnerFailureInfo;
export type RunStatusInfo = ApiModels.RunStatusInfo;

export type MemberCatalogItem = ApiModels.MemberCatalogItem;
export type CatalogGroupInfo = ApiModels.CatalogGroupInfo;
export type CatalogMemberInfo = ApiModels.CatalogMemberInfo;
export type CatalogTargetInfo = ApiModels.CatalogTargetInfo;
export type CatalogInfo = ApiModels.CatalogInfo;
export type RunAcceptedResponse = ApiModels.RunAcceptedResponse;
export type PresetGateState = ApiModels.PresetGateState;
export type ScriptTaskRunResult = ApiModels.ScriptTaskRunResult;
export type TaskRecord = ApiModels.TaskRecord;
export type RequeueItem = ApiModels.RequeueItem;
export type RequeueToGatewayRequest = ApiModels.RequeueToGatewayRequest;
export type RequeueBatchResult = ApiModels.RequeueBatchResult;
export type RequeueItemResult = ApiModels.RequeueItemResult;
export type RequeueToGatewayResponse = ApiModels.RequeueToGatewayResponse;
export type OutputFileInfo = ApiModels.OutputFileInfo;
export type ExtraBankInfo = ApiModels.ExtraBankInfo;
export type WorkflowDashboardSnapshotResponse = ApiModels.WorkflowDashboard;
export type ServerTimeResponse = ApiModels.ServerTimeResponse;
export type DatabaseCredentialStatusResponse = ApiModels.DatabaseCredentialStatusResponse;
export type DatabaseOptionResponse = ApiModels.DatabaseOptionResponse;

export interface ProblemDetailsResponse extends ApiModels.ProblemDetails {
  errorCode?: string;
  traceId?: string;
  activeCorrelationId?: string;
}

export interface TaskUiState {
  running: boolean;
  startedAt: string | null;
  completedAt?: string | null;
  result: ScriptTaskRunResult | null;
  error: string | null;
  stale: boolean;
}

export interface MemberViewModel {
  key: string;
  memberCode: string;
  memberFileExtension: string | null;
  name: string;
  targetCodes: string[];
  selected: boolean;
}

export interface MemberGroupViewModel {
  id: number;
  name: string;
  folder: string;
  members: MemberViewModel[];
}
