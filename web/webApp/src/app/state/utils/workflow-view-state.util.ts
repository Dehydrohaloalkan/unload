import {
  ExtraBankInfo,
  PresetGateState,
  RunLifecycleStatus,
  RunStatusInfo,
} from '../../app.models';

export type WorkflowPhase = 'gate' | 'tasks';

export function buildExtraBankNamesByCode(banks: ExtraBankInfo[]): Record<string, string> {
  const names: Record<string, string> = {};
  for (const bank of banks) {
    if (bank.nrBank) {
      names[bank.nrBank] = bank.bankName;
    }
  }
  return names;
}

export function resolveExtraLastCompletedAt(
  activeExtraRun: RunStatusInfo | null,
  dashboardCompletedAt: string | null,
): string | null {
  return activeExtraRun?.status === RunLifecycleStatus.Completed
    ? activeExtraRun.updatedAt
    : dashboardCompletedAt;
}

export function canUseMainOrExtra(presetState: PresetGateState | null): boolean {
  return Boolean(
    presetState && (!presetState.requiresPresetExecution || presetState.presetCompleted),
  );
}

export function resolveWorkflowPhase(presetState: PresetGateState | null): WorkflowPhase {
  return presetState?.presetCompleted ? 'tasks' : 'gate';
}
