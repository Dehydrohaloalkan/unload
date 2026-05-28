import { inject } from '@angular/core';
import {
  signalStore,
  withMethods,
  withState,
  patchState,
} from '@ngrx/signals';
import { TaskUiState } from '../app.models';
import { t } from '../i18n/i18n';
import { AdminStore } from './admin.store';
import { ApiClientService } from './api-client.service';
import { WorkflowErrorStore } from './error.store';
import { BROWSER_STORAGE } from './storage.token';
import {
  createIdleTaskState,
  restoreTaskState,
  runTaskAsync,
} from './utils/task-runner.util';

const EXTRA_TASK_STORAGE_KEY = 'unload.web.extra-task';

interface ExtraState {
  extraTask: TaskUiState;
  publishExtraToGateway: boolean;
}

const INITIAL: ExtraState = {
  extraTask: createIdleTaskState(),
  publishExtraToGateway: true,
};

export const ExtraStore = signalStore(
  { providedIn: 'root' },
  withState(INITIAL),
  withMethods((store) => {
    const api = inject(ApiClientService);
    const admin = inject(AdminStore);
    const errorStore = inject(WorkflowErrorStore);
    const storage = inject(BROWSER_STORAGE);

    const setTask = (next: TaskUiState): void => patchState(store, { extraTask: next });

    return {
      restore(): void {
        restoreTaskState({
          setState: setTask,
          storage,
          storageKey: EXTRA_TASK_STORAGE_KEY,
        });
      },
      setPublishExtraToGateway(enabled: boolean): void {
        patchState(store, { publishExtraToGateway: Boolean(enabled) });
      },
      runExtraAsync(): Promise<void> {
        return runTaskAsync(
          {
            setState: setTask,
            errorStore,
            storage,
            storageKey: EXTRA_TASK_STORAGE_KEY,
          },
          () => api.runExtra(admin.adminMode(), store.publishExtraToGateway()),
          t('errors.extraFailed'),
        );
      },
    };
  }),
);

export type ExtraStore = InstanceType<typeof ExtraStore>;
