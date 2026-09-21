import {
  FileRunStage,
  MemberRunLifecycleStatus,
  RunLifecycleStatus,
  RunnerStep,
  ScriptRunStage,
  SenderBatchStatus,
} from '../../app.models';
import { t } from '../../i18n/i18n';
import { I18nKey } from '../../i18n/ru';

// Лейблы статусов/шагов резолвятся через i18n. Здесь — только enum → ключ; текст в ru.ts.
const RUN_STATUS_KEYS: Record<RunLifecycleStatus, I18nKey> = {
  [RunLifecycleStatus.Running]: 'status.run.running',
  [RunLifecycleStatus.Completed]: 'status.run.completed',
  [RunLifecycleStatus.Failed]: 'status.run.failed',
  [RunLifecycleStatus.Cancelled]: 'status.run.cancelled',
  [RunLifecycleStatus.CancellationRequested]: 'status.run.cancellationRequested',
};

const MEMBER_STATUS_KEYS: Record<MemberRunLifecycleStatus, I18nKey> = {
  [MemberRunLifecycleStatus.Pending]: 'status.member.pending',
  [MemberRunLifecycleStatus.Running]: 'status.member.running',
  [MemberRunLifecycleStatus.Completed]: 'status.member.completed',
  [MemberRunLifecycleStatus.Failed]: 'status.member.failed',
  [MemberRunLifecycleStatus.Cancelled]: 'status.member.cancelled',
};

const SENDER_STATUS_KEYS: Record<SenderBatchStatus, I18nKey> = {
  [SenderBatchStatus.Ready]: 'status.sender.ready',
  [SenderBatchStatus.InProgress]: 'status.sender.inProgress',
  [SenderBatchStatus.Completed]: 'status.sender.completed',
  [SenderBatchStatus.Failed]: 'status.sender.failed',
  [SenderBatchStatus.SkippedByRequest]: 'status.sender.skipped',
};

const SCRIPT_STAGE_KEYS: Record<ScriptRunStage, I18nKey> = {
  [ScriptRunStage.AwaitingWorker]: 'status.script.awaitingWorker',
  [ScriptRunStage.Running]: 'status.script.running',
  [ScriptRunStage.Completed]: 'status.script.completed',
  [ScriptRunStage.Failed]: 'status.script.failed',
  [ScriptRunStage.Cancelled]: 'status.script.cancelled',
};

const FILE_STAGE_KEYS: Record<FileRunStage, I18nKey> = {
  [FileRunStage.QueuedForWrite]: 'status.file.queuedForWrite',
  [FileRunStage.Written]: 'status.file.written',
  [FileRunStage.Failed]: 'status.file.failed',
  [FileRunStage.Cancelled]: 'status.file.cancelled',
};

const RUNNER_STEP_KEYS: Record<RunnerStep, I18nKey> = {
  [RunnerStep.RequestAccepted]: 'runner.steps.requestAccepted',
  [RunnerStep.TargetsResolved]: 'runner.steps.targetsResolved',
  [RunnerStep.ScriptDiscovered]: 'runner.steps.scriptDiscovered',
  [RunnerStep.QueryStarted]: 'runner.steps.queryStarted',
  [RunnerStep.QueryCompleted]: 'runner.steps.queryCompleted',
  [RunnerStep.ChunkCreated]: 'runner.steps.chunkCreated',
  [RunnerStep.FileWritten]: 'runner.steps.fileWritten',
  [RunnerStep.ScriptCompleted]: 'runner.steps.scriptCompleted',
  [RunnerStep.PublishedToGateway]: 'runner.steps.publishedToGateway',
  [RunnerStep.Completed]: 'runner.steps.completed',
  [RunnerStep.Failed]: 'runner.steps.failed',
  [RunnerStep.FileWriteStarted]: 'runner.steps.fileWriteStarted',
  [RunnerStep.GatewayBatchQueued]: 'runner.steps.gatewayBatchQueued',
};

export function resolveRunStatusLabel(status: RunLifecycleStatus | null | undefined): string {
  return status == null ? t('status.unknown') : t(RUN_STATUS_KEYS[status] ?? 'status.unknown');
}

export function resolveMemberStatusLabel(
  status: MemberRunLifecycleStatus | null | undefined,
): string {
  return status == null ? t('status.unknown') : t(MEMBER_STATUS_KEYS[status] ?? 'status.unknown');
}

export function resolveSenderStatusLabel(status: SenderBatchStatus | null | undefined): string {
  return status == null ? t('status.unknown') : t(SENDER_STATUS_KEYS[status] ?? 'status.unknown');
}

export function resolveScriptStageLabel(stage: ScriptRunStage | null | undefined): string {
  return stage == null ? t('status.unknown') : t(SCRIPT_STAGE_KEYS[stage] ?? 'status.unknown');
}

export function resolveFileStageLabel(stage: FileRunStage | null | undefined): string {
  return stage == null ? t('status.unknown') : t(FILE_STAGE_KEYS[stage] ?? 'status.unknown');
}

export function resolveRunnerStepLabel(step: RunnerStep | null | undefined): string {
  return step == null ? t('status.unknown') : t(RUNNER_STEP_KEYS[step] ?? 'status.unknown');
}

export type SeveritySlug = 'success' | 'info' | 'warn' | 'danger' | 'secondary';

export function resolveRunSeverity(status: RunLifecycleStatus | null | undefined): SeveritySlug {
  switch (status) {
    case RunLifecycleStatus.Completed:
      return 'success';
    case RunLifecycleStatus.Failed:
      return 'danger';
    case RunLifecycleStatus.Cancelled:
    case RunLifecycleStatus.CancellationRequested:
      return 'warn';
    case RunLifecycleStatus.Running:
      return 'info';
    default:
      return 'secondary';
  }
}

export function resolveMemberSeverity(status: MemberRunLifecycleStatus): SeveritySlug {
  switch (status) {
    case MemberRunLifecycleStatus.Completed:
      return 'success';
    case MemberRunLifecycleStatus.Running:
      return 'info';
    case MemberRunLifecycleStatus.Failed:
      return 'danger';
    case MemberRunLifecycleStatus.Cancelled:
      return 'warn';
    default:
      return 'secondary';
  }
}
