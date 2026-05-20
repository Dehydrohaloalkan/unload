import { computed, inject } from '@angular/core';
import {
  signalStore,
  withComputed,
  withMethods,
  withState,
  patchState,
} from '@ngrx/signals';
import { PresetGateState, TaskUiState } from '../app.models';
import { AdminStore } from './admin.store';
import { ApiClientService } from './api-client.service';
import { WorkflowErrorStore } from './error.store';
import { BROWSER_STORAGE } from './storage.token';
import {
  createIdleTaskState,
  restoreTaskState,
  runTaskAsync,
} from './utils/task-runner.util';

const PRESET_TASK_STORAGE_KEY = 'unload.web.preset-task';

interface PresetState {
  presetState: PresetGateState | null;
  presetTask: TaskUiState;
}

const INITIAL: PresetState = {
  presetState: null,
  presetTask: createIdleTaskState(),
};

export const PresetStore = signalStore(
  { providedIn: 'root' },
  withState(INITIAL),
  withComputed(({ presetTask }) => ({
    presetCompletedAt: computed(() => {
      const task = presetTask();
      return task.result && task.completedAt ? task.completedAt : null;
    }),
  })),
  withMethods((store) => {
    const api = inject(ApiClientService);
    const admin = inject(AdminStore);
    const errorStore = inject(WorkflowErrorStore);
    const storage = inject(BROWSER_STORAGE);

    const setTask = (next: TaskUiState): void => patchState(store, { presetTask: next });

    const refreshState = async (): Promise<void> => {
      const state = await api.fetchPresetState();
      patchState(store, { presetState: state });
    };

    return {
      restore(): void {
        restoreTaskState({
          setState: setTask,
          storage,
          storageKey: PRESET_TASK_STORAGE_KEY,
        });
      },
      setPresetState(value: PresetGateState | null): void {
        patchState(store, { presetState: value });
      },
      canRunPreset(isRunBusy: boolean): boolean {
        const preset = store.presetState();
        return (
          !!preset &&
          preset.readyForPreset &&
          !preset.presetCompleted &&
          !store.presetTask().running &&
          !isRunBusy
        );
      },
      refreshState,
      runPresetAsync(adminMode: boolean): Promise<void> {
        return runTaskAsync(
          {
            setState: setTask,
            errorStore,
            storage,
            storageKey: PRESET_TASK_STORAGE_KEY,
            onSuccess: refreshState,
          },
          () => api.runPreset(adminMode || admin.adminMode()),
          'Не удалось запустить preset-задачу.',
        );
      },
    };
  }),
);

export type PresetStore = InstanceType<typeof PresetStore>;
