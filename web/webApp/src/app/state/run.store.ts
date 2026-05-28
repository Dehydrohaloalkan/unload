import { isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, PLATFORM_ID, computed, effect, inject } from '@angular/core';
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
  ProblemDetailsResponse,
  RequeueItem,
  RequeueToGatewayResponse,
  RunStatusInfo,
  RunnerEvent,
} from '../app.models';
import { t } from '../i18n/i18n';
import { AdminStore } from './admin.store';
import { ApiClientService } from './api-client.service';
import { CatalogStore } from './catalog.store';
import { DashboardStore } from './dashboard.store';
import { WorkflowErrorStore } from './error.store';
import { RealtimeHubService } from './realtime-hub.service';
import { SelectionStore } from './selection.store';
import { toErrorMessage } from './utils/error-message.util';
import { isRunStatusPayload, isTerminalRunStatus } from './utils/run-status.util';

const RUN_EVENT_LIMIT = 80;
const POLL_INTERVAL_MS = 2500;

interface RunStoreState {
  activeRun: RunStatusInfo | null;
  trackedCorrelationId: string | null;
  runEvents: RunnerEvent[];
  publishRunToGateway: boolean;
  requeueRunning: boolean;
  requeueResult: RequeueToGatewayResponse | null;
}

const INITIAL: RunStoreState = {
  activeRun: null,
  trackedCorrelationId: null,
  runEvents: [],
  publishRunToGateway: true,
  requeueRunning: false,
  requeueResult: null,
};

export const RunStore = signalStore(
  { providedIn: 'root' },
  withState(INITIAL),
  withComputed(({ activeRun, trackedCorrelationId }) => ({
    isRunBusy: computed(() => {
      const run = activeRun();
      return !!run && !isTerminalRunStatus(run.status);
    }),
    trackedId: computed(() => trackedCorrelationId()),
  })),
  withMethods((store) => {
    const api = inject(ApiClientService);
    const catalog = inject(CatalogStore);
    const selection = inject(SelectionStore);
    const dashboard = inject(DashboardStore);
    const admin = inject(AdminStore);
    const errorStore = inject(WorkflowErrorStore);
    const hub = inject(RealtimeHubService);
    const destroyRef = inject(DestroyRef);
    const platformId = inject(PLATFORM_ID);
    const browser = isPlatformBrowser(platformId);

    let pollTimerId: number | null = null;

    const stopPolling = (): void => {
      if (!browser || pollTimerId === null) return;
      window.clearInterval(pollTimerId);
      pollTimerId = null;
    };

    const refreshTrackedRunAsync = async (): Promise<void> => {
      const correlationId = store.trackedCorrelationId();
      if (!correlationId) return;

      const state = await api.fetchRunStatus(correlationId);
      if (!state) {
        patchState(store, { trackedCorrelationId: null, activeRun: null });
        return;
      }

      patchState(store, { activeRun: state });
      if (isTerminalRunStatus(state.status)) {
        await dashboard.refreshTodayRunsAsync();
      }
    };

    const ensurePolling = (): void => {
      if (!browser || pollTimerId !== null) return;
      pollTimerId = window.setInterval(() => void refreshTrackedRunAsync(), POLL_INTERVAL_MS);
    };

    destroyRef.onDestroy(() => stopPolling());

    const adoptCorrelationId = async (correlationId: string): Promise<void> => {
      patchState(store, { trackedCorrelationId: correlationId });
      await hub.subscribeRun(correlationId);
      const run = await api.fetchRunStatus(correlationId);
      if (run) {
        patchState(store, { activeRun: run });
      }
    };

    const handleConflict = async (error: HttpErrorResponse): Promise<void> => {
      const details = (error.error ?? {}) as ProblemDetailsResponse;
      if (details.activeCorrelationId) {
        await adoptCorrelationId(details.activeCorrelationId);
      }
      errorStore.setError(details.detail ?? t('errors.activeRunConflict'));
    };

    return {
      setPublishRunToGateway(enabled: boolean): void {
        patchState(store, { publishRunToGateway: Boolean(enabled) });
      },

      applyInitialActiveRun(
        payload: RunStatusInfo | { correlationId: string | null } | null,
      ): { correlationId: string | null; status: RunStatusInfo | null } {
        const correlationId = payload?.correlationId ?? null;
        patchState(store, {
          trackedCorrelationId: correlationId,
          activeRun: null,
          runEvents: [],
        });
        return {
          correlationId,
          status: isRunStatusPayload(payload) ? payload : null,
        };
      },

      async syncActiveRunAsync(
        correlationId: string,
        knownStatus: RunStatusInfo | null,
      ): Promise<void> {
        await hub.subscribeRun(correlationId);
        const run = knownStatus ?? (await api.fetchRunStatus(correlationId));
        if (run) {
          patchState(store, { activeRun: run });
        }
      },

      async startRunAsync(): Promise<void> {
        errorStore.clear();
        patchState(store, { runEvents: [] });

        try {
          const response = await api.startRun({
            targetCodes: selection.selectedTargetCodes(),
            memberCodes: selection.resolveSelectedMemberCodes(catalog.catalog()),
            adminOverride: admin.adminMode(),
            publishToGateway: store.publishRunToGateway(),
          });
          await adoptCorrelationId(response.correlationId);
        } catch (error) {
          if (error instanceof HttpErrorResponse && error.status === 409) {
            await handleConflict(error);
            return;
          }
          errorStore.setError(toErrorMessage(error, t('errors.runStartFailed')));
        }
      },

      async stopRunAsync(): Promise<void> {
        const correlationId = store.trackedCorrelationId();
        if (!correlationId || !store.isRunBusy()) {
          return;
        }
        errorStore.clear();
        try {
          await api.stopRun(correlationId);
        } catch (error) {
          errorStore.setError(toErrorMessage(error, t('errors.runStopFailed')));
        }
      },

      async requeueToGatewayAsync(items: RequeueItem[]): Promise<void> {
        if (!items || items.length === 0) {
          return;
        }
        errorStore.clear();
        patchState(store, { requeueRunning: true, requeueResult: null });
        try {
          const response = await api.requeueToGateway(items);
          patchState(store, { requeueResult: response ?? null });
          await dashboard.refreshDashboardAsync();
        } catch (error) {
          errorStore.setError(toErrorMessage(error, t('errors.requeueFailed')));
        } finally {
          patchState(store, { requeueRunning: false });
        }
      },

      _trackStatusEvents: rxMethod<RunnerEvent>(
        tap((event) =>
          patchState(store, (current) => ({
            runEvents: [event, ...current.runEvents].slice(0, RUN_EVENT_LIMIT),
          })),
        ),
      ),

      _trackRunStatus: rxMethod<RunStatusInfo>(
        tap((status) => {
          patchState(store, {
            trackedCorrelationId: status.correlationId,
            activeRun: status,
          });
          if (isTerminalRunStatus(status.status)) {
            dashboard.markRunCompleted(status);
            void dashboard.refreshTodayRunsAsync();
            void dashboard.refreshDashboardAsync();
          }
        }),
      ),

      _trackReconnect: rxMethod<void>(
        tap(() => {
          void hub.subscribeRun(store.trackedCorrelationId());
        }),
      ),

      _ensurePollingIfNeeded(): void {
        ensurePolling();
      },

      _stopPollingIfRunning(): void {
        stopPolling();
      },
    };
  }),
  withHooks({
    onInit(store) {
      const hub = inject(RealtimeHubService);

      store._trackStatusEvents(hub.statusEvents$);
      store._trackRunStatus(hub.runStatusEvents$);
      store._trackReconnect(hub.reconnected$);

      // Polling — только когда hub отвалился И есть активный run, который надо отслеживать.
      effect(() => {
        const shouldPoll =
          store.isRunBusy() && !!store.trackedCorrelationId() && !hub.connectionReady();
        if (shouldPoll) {
          store._ensurePollingIfNeeded();
        } else {
          store._stopPollingIfRunning();
        }
      });
    },
  }),
);

export type RunStore = InstanceType<typeof RunStore>;
