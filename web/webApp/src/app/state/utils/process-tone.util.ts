import {
  FileRunStage,
  MemberRunLifecycleStatus,
  ScriptRunStage,
  SenderBatchStatus,
} from '../../app.models';

/** Presentation-only semantic tone. It must never encode elapsed-time or SLA assumptions. */
export type ProcessTone = 'active' | 'success' | 'danger' | 'neutral';

export function resolveMemberTone(
  status: MemberRunLifecycleStatus | null | undefined,
): ProcessTone {
  switch (status) {
    case MemberRunLifecycleStatus.Running:
      return 'active';
    case MemberRunLifecycleStatus.Completed:
      return 'success';
    case MemberRunLifecycleStatus.Failed:
      return 'danger';
    case MemberRunLifecycleStatus.Pending:
    case MemberRunLifecycleStatus.Cancelled:
    default:
      return 'neutral';
  }
}

export function resolveScriptTone(stage: ScriptRunStage | null | undefined): ProcessTone {
  switch (stage) {
    case ScriptRunStage.AwaitingWorker:
    case ScriptRunStage.Running:
      return 'active';
    case ScriptRunStage.Completed:
      return 'success';
    case ScriptRunStage.Failed:
      return 'danger';
    case ScriptRunStage.Cancelled:
    default:
      return 'neutral';
  }
}

export function resolveFileTone(stage: FileRunStage | null | undefined): ProcessTone {
  switch (stage) {
    case FileRunStage.QueuedForWrite:
      return 'active';
    case FileRunStage.Written:
      return 'success';
    case FileRunStage.Failed:
      return 'danger';
    case FileRunStage.Cancelled:
    default:
      return 'neutral';
  }
}

export function resolveBatchTone(status: SenderBatchStatus | null | undefined): ProcessTone {
  switch (status) {
    case SenderBatchStatus.Ready:
    case SenderBatchStatus.InProgress:
      return 'active';
    case SenderBatchStatus.Completed:
      return 'success';
    case SenderBatchStatus.Failed:
      return 'danger';
    case SenderBatchStatus.SkippedByRequest:
    default:
      return 'neutral';
  }
}

/** A stage reflects its most actionable card: failure, then active work, then completion. */
export function resolveStageTone(tones: Iterable<ProcessTone>): ProcessTone {
  let highest: ProcessTone = 'neutral';
  for (const tone of tones) {
    if (tone === 'danger') return 'danger';
    if (tone === 'active') highest = 'active';
    else if (tone === 'success' && highest === 'neutral') highest = 'success';
  }
  return highest;
}
