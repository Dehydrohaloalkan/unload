import { inject, isDevMode } from '@angular/core';
import {
  signalStore,
  withMethods,
  withState,
  patchState,
} from '@ngrx/signals';
import { ExtraBankInfo, TaskUiState } from '../app.models';
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
  banks: ExtraBankInfo[];
  banksLoaded: boolean;
  banksLoading: boolean;
  // null — выбраны все банки (запуск базовых скриптов); массив — подмножество (atomic-скрипты).
  selectedBankCodes: string[] | null;
}

const INITIAL: ExtraState = {
  extraTask: createIdleTaskState(),
  publishExtraToGateway: true,
  banks: [],
  banksLoaded: false,
  banksLoading: false,
  selectedBankCodes: null,
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

    const allBankCodes = (): string[] => store.banks().map((bank) => bank.nrBank);

    // Эффективный набор выбранных кодов: null означает «все».
    const effectiveSelected = (): Set<string> => {
      const selected = store.selectedBankCodes();
      return new Set(selected ?? allBankCodes());
    };

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
      async loadBanksAsync(force = false): Promise<void> {
        if (store.banksLoading() || (store.banksLoaded() && !force)) {
          return;
        }
        patchState(store, { banksLoading: true });
        try {
          const banks = await api.fetchExtraBanks();
          patchState(store, { banks, banksLoaded: true });
        } catch (error) {
          if (isDevMode()) {
            console.error(error);
          }
        } finally {
          patchState(store, { banksLoading: false });
        }
      },
      isBankSelected(code: string): boolean {
        return effectiveSelected().has(code);
      },
      allBanksSelected(): boolean {
        return store.selectedBankCodes() === null;
      },
      toggleBank(code: string, selected: boolean): void {
        const next = effectiveSelected();
        if (selected) {
          next.add(code);
        } else {
          next.delete(code);
        }
        const codes = allBankCodes();
        const isAll = codes.length > 0 && codes.every((c) => next.has(c));
        patchState(store, {
          selectedBankCodes: isAll ? null : codes.filter((c) => next.has(c)),
        });
      },
      selectAllBanks(): void {
        patchState(store, { selectedBankCodes: null });
      },
      runExtraAsync(): Promise<void> {
        const selected = store.selectedBankCodes();
        // Все банки (null) → null; подмножество → массив выбранных кодов.
        const payloadBanks = selected === null ? null : selected;
        return runTaskAsync(
          {
            setState: setTask,
            errorStore,
            storage,
            storageKey: EXTRA_TASK_STORAGE_KEY,
          },
          () => api.runExtra(admin.adminMode(), store.publishExtraToGateway(), payloadBanks),
          t('errors.extraFailed'),
        );
      },
    };
  }),
);

export type ExtraStore = InstanceType<typeof ExtraStore>;
