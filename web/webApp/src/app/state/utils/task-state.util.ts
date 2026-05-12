import { TaskUiState } from '../../app.models';

export function createIdleTaskState(): TaskUiState {
  return {
    running: false,
    startedAt: null,
    completedAt: null,
    result: null,
    error: null,
    stale: false,
  };
}

export function normalizeRestoredTaskState(payload: Partial<TaskUiState> | null): TaskUiState {
  if (!payload) {
    return createIdleTaskState();
  }

  return {
    running: false,
    startedAt: typeof payload.startedAt === 'string' ? payload.startedAt : null,
    completedAt: typeof payload.completedAt === 'string' ? payload.completedAt : null,
    result: payload.result ?? null,
    error: payload.error ?? null,
    stale: Boolean(payload.running),
  };
}
