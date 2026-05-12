import {
  MemberRunLifecycleStatus,
  RunLifecycleStatus,
  RunnerStep,
  SenderBatchStatus,
} from '../../app.models';

const RUN_STATUS_LABELS: Record<RunLifecycleStatus, string> = {
  [RunLifecycleStatus.Running]: 'Running',
  [RunLifecycleStatus.Completed]: 'Completed',
  [RunLifecycleStatus.Failed]: 'Failed',
  [RunLifecycleStatus.Cancelled]: 'Cancelled',
  [RunLifecycleStatus.CancellationRequested]: 'Cancellation requested',
};

const MEMBER_STATUS_LABELS: Record<MemberRunLifecycleStatus, string> = {
  [MemberRunLifecycleStatus.Pending]: 'Pending',
  [MemberRunLifecycleStatus.Running]: 'Running',
  [MemberRunLifecycleStatus.Completed]: 'Completed',
  [MemberRunLifecycleStatus.Failed]: 'Failed',
  [MemberRunLifecycleStatus.Cancelled]: 'Cancelled',
};

const SENDER_STATUS_LABELS: Record<SenderBatchStatus, string> = {
  [SenderBatchStatus.Ready]: 'Ready',
  [SenderBatchStatus.InProgress]: 'In progress',
  [SenderBatchStatus.Completed]: 'Completed',
  [SenderBatchStatus.Failed]: 'Failed',
  [SenderBatchStatus.SkippedByRequest]: 'Skipped',
};

const RUNNER_STEP_LABELS: Record<RunnerStep, string> = {
  [RunnerStep.RequestAccepted]: 'Request accepted',
  [RunnerStep.TargetsResolved]: 'Targets resolved',
  [RunnerStep.ScriptDiscovered]: 'Script discovered',
  [RunnerStep.QueryStarted]: 'Query started',
  [RunnerStep.QueryCompleted]: 'Query completed',
  [RunnerStep.ChunkCreated]: 'Chunk created',
  [RunnerStep.FileWritten]: 'File written',
  [RunnerStep.ScriptCompleted]: 'Script completed',
  [RunnerStep.PublishedToMq]: 'Published to MQ',
  [RunnerStep.Completed]: 'Completed',
  [RunnerStep.Failed]: 'Failed',
};

export function resolveRunStatusLabel(status: RunLifecycleStatus | null | undefined): string {
  return status == null ? 'Unknown' : (RUN_STATUS_LABELS[status] ?? 'Unknown');
}

export function resolveMemberStatusLabel(
  status: MemberRunLifecycleStatus | null | undefined,
): string {
  return status == null ? 'Unknown' : (MEMBER_STATUS_LABELS[status] ?? 'Unknown');
}

export function resolveSenderStatusLabel(status: SenderBatchStatus | null | undefined): string {
  return status == null ? 'Unknown' : (SENDER_STATUS_LABELS[status] ?? 'Unknown');
}

export function resolveRunnerStepLabel(step: RunnerStep | null | undefined): string {
  return step == null ? 'Unknown' : (RUNNER_STEP_LABELS[step] ?? 'Unknown');
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
