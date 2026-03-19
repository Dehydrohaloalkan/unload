export enum RunLifecycleStatus {
  Running = 0,
  Completed = 1,
  Failed = 2,
  Cancelled = 3,
  CancellationRequested = 4
}

export enum MemberRunLifecycleStatus {
  Pending = 0,
  Running = 1,
  Completed = 2,
  Failed = 3,
  Cancelled = 4
}

export enum RunnerStep {
  RequestAccepted = 0,
  TargetsResolved = 1,
  ScriptDiscovered = 2,
  QueryStarted = 3,
  QueryCompleted = 4,
  ChunkCreated = 5,
  FileWritten = 6,
  ScriptCompleted = 7,
  PublishedToMq = 8,
  Completed = 9,
  Failed = 10
}

export interface MemberRunStatusInfo {
  memberName: string;
  status: MemberRunLifecycleStatus;
  lastStep: RunnerStep | null;
  message: string | null;
  updatedAt: string;
}

export interface RunWorkerStatusInfo {
  workerId: number;
  state: string;
  scriptCode: string | null;
  memberName: string | null;
  updatedAt: string;
}

export interface RunOutputArtifactInfo {
  fileName: string;
  filePath: string;
  memberName: string | null;
  scriptCode: string | null;
  occurredAt: string;
}

export interface RunStatusInfo {
  correlationId: string;
  status: RunLifecycleStatus;
  targetCodes: string[];
  createdAt: string;
  updatedAt: string;
  lastStep: RunnerStep | null;
  message: string | null;
  outputPath: string | null;
  memberStatuses: Record<string, MemberRunStatusInfo> | null;
  outputArtifacts: RunOutputArtifactInfo[] | null;
  workerStatuses: Record<number, RunWorkerStatusInfo> | null;
}

export interface RunnerEvent {
  occurredAt: string;
  correlationId: string;
  step: RunnerStep;
  message: string;
  targetCode: string | null;
  memberName: string | null;
  scriptCode: string | null;
  records: number | null;
  filePath: string | null;
  workerId: number | null;
}

export interface MemberCatalogItem {
  code: string;
  name: string;
  targetCodes: string[];
  activeRunCorrelationId: string | null;
  activeRunStatus: MemberRunStatusInfo | null;
}

export interface CatalogGroupInfo {
  id: number;
  name: string;
  folder: string;
  code: string;
}

export interface CatalogMemberInfo {
  id: number;
  name: string;
  code: string;
  fileExtension: string;
}

export interface CatalogTargetInfo {
  targetCode: string;
  groupId: number;
  memberId: number;
  groupName: string;
  groupFolder: string;
  groupCode: string;
  memberName: string;
  memberCode: string;
  memberFileExtension: string;
}

export interface CatalogInfo {
  groups: CatalogGroupInfo[];
  members: CatalogMemberInfo[];
  targets: CatalogTargetInfo[];
  bigScriptTargetCodes: string[];
}

export interface RunAcceptedResponse {
  correlationId: string;
  runStatusPath: string;
  hubPath: string;
  subscribeMethod: string;
  eventName: string;
  runStatusEventName: string;
  stopPath: string;
}

export interface PresetGateState {
  enabled: boolean;
  pollingStarted: boolean;
  requiresPresetExecution: boolean;
  readyForPreset: boolean;
  presetCompleted: boolean;
  lastProbeValue: number | null;
  lastProbeAt: string | null;
  message: string;
}

export interface ScriptTaskRunResult {
  taskName: string;
  correlationId: string;
  scriptsExecuted: number;
  filesWritten: number;
  outputPath: string | null;
  message: string;
}

export interface ServerTimeResponse {
  serverLocalTime: string;
  serverUtcTime: string;
  utcOffsetMinutes: number;
  timeZoneId: string;
}

export interface ProblemDetailsResponse {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errorCode?: string;
  traceId?: string;
  activeCorrelationId?: string;
}

export interface TaskUiState {
  running: boolean;
  startedAt: string | null;
  result: ScriptTaskRunResult | null;
  error: string | null;
  stale: boolean;
}

export interface MemberLogLine {
  time: string;
  step: RunnerStep;
  message: string;
}

export interface MemberViewModel {
  code: string;
  name: string;
  targetCodes: string[];
  selected: boolean;
  status: MemberRunLifecycleStatus;
  lastStep: RunnerStep | null;
  message: string | null;
  updatedAt: string | null;
  logs: MemberLogLine[];
  outputArtifacts: RunOutputArtifactInfo[];
}

export interface MemberGroupViewModel {
  id: number;
  name: string;
  folder: string;
  members: MemberViewModel[];
}
