import { isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, PLATFORM_ID, computed, effect, inject, isDevMode } from '@angular/core';
import {
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withState,
  patchState,
} from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { tap } from 'rxjs';
import {
  ExtraBankInfo,
  ProblemDetailsResponse,
  RunStatusInfo,
  TaskUiState,
} from '../app.models';
import { t } from '../i18n/i18n';
import { AdminStore } from './admin.store';
import { ApiClientService } from './api-client.service';
import { DashboardStore } from './dashboard.store';
import { WorkflowErrorStore } from './error.store';
import { RealtimeHubService } from './realtime-hub.service';
import { isTerminalRunStatus } from './utils/run-status.util';

const POLL_INTERVAL_MS = 2500;

const isExtraStatus = (status: RunStatusInfo): boolean =>
  (status.taskCode ?? '').trim().toLowerCase() === 'extra';

interface ExtraState {
  // Активная/последняя extra-выгрузка как серверная запись (источник истины, переживает refresh).
  activeExtraRun: RunStatusInfo | null;
  trackedExtraId: string | null;
  publishExtraToGateway: boolean;
  banks: ExtraBankInfo[];
  banksLoaded: boolean;
  banksLoading: boolean;
  // null — выбраны все банки (запуск базовых скриптов); массив — подмножество (atomic-скрипты).
  selectedBankCodes: string[] | null;
}

const INITIAL: ExtraState = {
  activeExtraRun: null,
  trackedExtraId: null,
  publishExtraToGateway: true,
  banks: [],
  banksLoaded: false,
  banksLoading: false,
  selectedBankCodes: null,
};

export const ExtraStore = signalStore(
  { providedIn: 'root' },
  withState(INITIAL),
  withComputed(({ activeExtraRun }) => ({
    isExtraBusy: computed<boolean>(() => {
      const run = activeExtraRun();
      return !!run && !isTerminalRunStatus(run.status);
    }),
    // Совместимый с карточкой TaskUiState: карточке нужен только флаг running (спиннер/молния).
    extraTask: computed<TaskUiState>(() => {
      const run = activeExtraRun();
      const running = !!run && !isTerminalRunStatus(run.status);
      return {
        running,
        startedAt: run?.createdAt ?? null,
        completedAt: run && !running ? run.updatedAt : null,
        result: null,
        error: null,
        stale: false,
      };
    }),
  })),
  withMethods((store) => {
    const api = inject(ApiClientService);
    const admin = inject(AdminStore);
    const errorStore = inject(WorkflowErrorStore);
    const dashboard = inject(DashboardStore);
    const hub = inject(RealtimeHubService);
    const platformId = inject(PLATFORM_ID);
    const browser = isPlatformBrowser(platformId);

    let pollTimerId: number | null = null;

    const allBankCodes = (): string[] => store.banks().map((bank) => bank.nrBank);

    // Эффективный набор выбранных кодов: null означает «все».
    const effectiveSelected = (): Set<string> => {
      const selected = store.selectedBankCodes();
      return new Set(selected ?? allBankCodes());
    };

    const stopPolling = (): void => {
      if (!browser || pollTimerId === null) return;
      window.clearInterval(pollTimerId);
      pollTimerId = null;
    };

    const refreshTrackedExtraAsync = async (): Promise<void> => {
      const correlationId = store.trackedExtraId();
      if (!correlationId) return;
      const state = await api.fetchRunStatus(correlationId);
      if (!state) return;
      patchState(store, { activeExtraRun: state });
      if (isTerminalRunStatus(state.status)) {
        await dashboard.refreshDashboardAsync();
      }
    };

    const ensurePolling = (): void => {
      if (!browser || pollTimerId !== null) return;
      pollTimerId = window.setInterval(() => void refreshTrackedExtraAsync(), POLL_INTERVAL_MS);
    };

    return {
      _stopPolling: stopPolling,
      _ensurePolling: ensurePolling,

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

      /** Подхватывает активную extra-выгрузку из списка сегодняшних запусков (bootstrap/refresh). */
      adoptActiveFromRuns(runs: RunStatusInfo[]): void {
        const active = (runs ?? [])
          .filter((run) => isExtraStatus(run) && !isTerminalRunStatus(run.status))
          .sort((a, b) => (b.createdAt ?? '').localeCompare(a.createdAt ?? ''))[0];
        if (active) {
          patchState(store, { trackedExtraId: active.correlationId, activeExtraRun: active });
        }
      },

      async runExtraAsync(): Promise<void> {
        errorStore.clear();
        const selected = store.selectedBankCodes();
        // Все банки (null) → null; подмножество → массив выбранных кодов.
        const payloadBanks = selected === null ? null : selected;
        try {
          const response = await api.runExtra(
            admin.adminMode(),
            store.publishExtraToGateway(),
            payloadBanks,
          );
          patchState(store, {
            trackedExtraId: response.correlationId,
            activeExtraRun: await api.fetchRunStatus(response.correlationId),
          });
        } catch (error) {
          errorStore.setError(toExtraError(error));
        }
      },

      async stopExtraAsync(): Promise<void> {
        const correlationId = store.trackedExtraId();
        if (!correlationId || !store.isExtraBusy()) {
          return;
        }
        errorStore.clear();
        try {
          await api.stopRun(correlationId);
        } catch (error) {
          errorStore.setError(toExtraError(error));
        }
      },

      _trackExtraStatus: rxMethod<RunStatusInfo>(
        tap((status) => {
          if (!isExtraStatus(status)) {
            return;
          }
          patchState(store, {
            trackedExtraId: status.correlationId,
            activeExtraRun: status,
          });
          if (isTerminalRunStatus(status.status)) {
            void dashboard.refreshDashboardAsync();
          }
        }),
      ),
    };
  }),
  withHooks({
    onInit(store) {
      const hub = inject(RealtimeHubService);
      const destroyRef = inject(DestroyRef);

      store._trackExtraStatus(hub.runStatusEvents$);
      destroyRef.onDestroy(() => store._stopPolling());

      // Polling — только когда хаб отвалился и есть активная extra-выгрузка.
      effect(() => {
        const shouldPoll = store.isExtraBusy() && !!store.trackedExtraId() && !hub.connectionReady();
        if (shouldPoll) {
          store._ensurePolling();
        } else {
          store._stopPolling();
        }
      });
    },
  }),
);

function toExtraError(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const details = (error.error ?? {}) as ProblemDetailsResponse;
    if (details.detail) {
      return details.detail;
    }
  }
  return t('errors.extraFailed');
}

export type ExtraStore = InstanceType<typeof ExtraStore>;
