import { RunLifecycleStatus, RunStatusInfo } from '../../app.models';

export function isTerminalRunStatus(status: RunLifecycleStatus): boolean {
  return (
    status === RunLifecycleStatus.Completed ||
    status === RunLifecycleStatus.Failed ||
    status === RunLifecycleStatus.Cancelled
  );
}

export function isRunStatusPayload(payload: unknown): payload is RunStatusInfo {
  return !!payload && typeof payload === 'object' && 'createdAt' in (payload as object);
}

export function isMainRunHistoryEntry(run: RunStatusInfo): boolean {
  const taskCode = (run.taskCode ?? '').trim().toLowerCase();
  const correlationId = (run.correlationId ?? '').trim().toLowerCase();
  return taskCode === 'run' && correlationId.startsWith('req-');
}

export function isExtraRunEntry(run: RunStatusInfo): boolean {
  return (run.taskCode ?? '').trim().toLowerCase() === 'extra';
}
