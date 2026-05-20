import { ScriptTaskRunResult, TaskUiState } from '../../app.models';
import { WorkflowErrorStore } from '../error.store';
import { readJson, writeJson } from './storage.util';
import { createIdleTaskState, normalizeRestoredTaskState } from './task-state.util';
import { toErrorMessage } from './error-message.util';

export interface TaskRunnerHooks {
  /** Применить новый task-state через patchState соответствующего стора. */
  setState: (next: TaskUiState) => void;
  /** Источник ошибок. */
  errorStore: WorkflowErrorStore;
  /** Storage и ключ для персистенса между перезагрузками. */
  storage: Storage | null;
  storageKey: string;
  /** Колбэк после успешного запуска (например, refresh). */
  onSuccess?: () => Promise<void> | void;
  /** Пробрасывать ли исключение наружу. По умолчанию да. */
  rethrow?: boolean;
}

export function restoreTaskState(hooks: Pick<TaskRunnerHooks, 'setState' | 'storage' | 'storageKey'>): void {
  const payload = readJson<Partial<TaskUiState>>(hooks.storage, hooks.storageKey);
  hooks.setState(normalizeRestoredTaskState(payload));
}

export async function runTaskAsync(
  hooks: TaskRunnerHooks,
  call: () => Promise<ScriptTaskRunResult>,
  fallbackMessage: string,
): Promise<void> {
  hooks.errorStore.clear();
  const startedAt = new Date().toISOString();

  const update = (next: TaskUiState): void => {
    hooks.setState(next);
    writeJson(hooks.storage, hooks.storageKey, next);
  };

  update({
    running: true,
    startedAt,
    completedAt: null,
    result: null,
    error: null,
    stale: false,
  });

  try {
    const result = await call();
    update({
      running: false,
      startedAt,
      completedAt: new Date().toISOString(),
      result,
      error: null,
      stale: false,
    });
    if (hooks.onSuccess) {
      await hooks.onSuccess();
    }
  } catch (error) {
    const message = toErrorMessage(error, fallbackMessage);
    update({
      running: false,
      startedAt,
      completedAt: new Date().toISOString(),
      result: null,
      error: message,
      stale: false,
    });
    hooks.errorStore.setError(message);
    if (hooks.rethrow !== false) {
      throw error;
    }
  }
}

export { createIdleTaskState };
