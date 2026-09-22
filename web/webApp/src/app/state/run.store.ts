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
import { isSameRunCorrelation, shouldAcceptRunStatus } from './utils/run-lifecycle.util';
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
  runLaunchPending: boolean;
  postInFlight: boolean;
  launchAttemptId: number | null;
  launchAttemptCorrelationId: string | null;
  awaitingActivationRelease: boolean;
}

const INITIAL: RunStoreState = {
  activeRun: null,
  trackedCorrelationId: null,
  runEvents: [],
  publishRunToGateway: true,
  requeueRunning: false,
  requeueResult: null,
  runLaunchPending: false,
  postInFlight: false,
  launchAttemptId: null,
  launchAttemptCorrelationId: null,
  awaitingActivationRelease: false,
};

export const RunStore = signalStore(
  { providedIn: 'root' },
  withState(INITIAL),
  withComputed(({ activeRun, trackedCorrelationId, runLaunchPending, awaitingActivationRelease }) => ({
    isRunBusy: computed(() => {
      const run = activeRun();
      return runLaunchPending() || awaitingActivationRelease() || (!!run && !isTerminalRunStatus(run.status));
    }),
    isRunLaunching: computed(() => runLaunchPending()),
    isAwaitingActivationRelease: computed(() => awaitingActivationRelease()),
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
    let nextLaunchAttemptId = 0;

    const ownsLaunchAttempt = (
      attemptId: number | null | undefined,
      correlationId?: string,
    ): boolean =>
      attemptId != null &&
      store.launchAttemptId() === attemptId &&
      (!correlationId || isSameRunCorrelation(store.launchAttemptCorrelationId(), correlationId));

    const clearLaunchAttempt = (attemptId: number): void => {
      if (!ownsLaunchAttempt(attemptId)) return;
      patchState(store, {
        runLaunchPending: false,
        launchAttemptId: null,
        launchAttemptCorrelationId: null,
      });
    };

    const stopPolling = (): void => {
      if (!browser || pollTimerId === null) return;
      window.clearInterval(pollTimerId);
      pollTimerId = null;
    };

    const checkActivationReleaseAsync = async (
      correlationId: string,
      attemptId?: number,
    ): Promise<void> => {
      if (!isSameRunCorrelation(store.trackedCorrelationId(), correlationId)) return;
      try {
        const activeRun = await api.fetchActiveRun();
        if (!isSameRunCorrelation(store.trackedCorrelationId(), correlationId)) return;
        // Keep the button disabled while any run still owns the channel. A
        // terminal status is intentionally not enough: history persistence and
        // hosted-service finalization happen before Complete() releases it.
        if (!activeRun) {
          const current = store.activeRun();
          patchState(store, {
            awaitingActivationRelease: false,
            activeRun: current && isTerminalRunStatus(current.status) ? current : null,
          });
          if (attemptId != null) clearLaunchAttempt(attemptId);
          return;
        }

        if (isSameRunCorrelation(activeRun.correlationId, correlationId)) {
          if (shouldAcceptRunStatus(store.activeRun(), activeRun, correlationId)) {
            patchState(store, {
              activeRun,
              awaitingActivationRelease: isTerminalRunStatus(activeRun.status),
            });
            if (attemptId != null) clearLaunchAttempt(attemptId);
          }
        }
      } catch {
        // Keep the conservative busy state; the interval retries this source of
        // truth without creating an unhandled rejection.
      }
    };

    const reconcileUnknownRunAsync = async (
      correlationId: string,
      attemptId?: number,
    ): Promise<void> => {
      if (!isSameRunCorrelation(store.trackedCorrelationId(), correlationId)) return;
      patchState(store, { awaitingActivationRelease: true });
      await checkActivationReleaseAsync(correlationId, attemptId);
    };

    const applyRunSnapshotAsync = async (
      correlationId: string,
      state: RunStatusInfo,
    ): Promise<void> => {
      if (!isSameRunCorrelation(store.trackedCorrelationId(), correlationId)) return;
      if (!shouldAcceptRunStatus(store.activeRun(), state, correlationId)) return;

      patchState(store, {
        activeRun: state,
        ...(isTerminalRunStatus(state.status) ? { awaitingActivationRelease: true } : {}),
      });
      const attemptId = store.launchAttemptId();
      if (attemptId != null && ownsLaunchAttempt(attemptId, correlationId)) {
        clearLaunchAttempt(attemptId);
      }
      if (isTerminalRunStatus(state.status)) {
        dashboard.markRunCompleted(state);
        await dashboard.refreshTodayRunsAsync();
        await checkActivationReleaseAsync(correlationId);
      }
    };

    const reconcileLaunchAttemptAsync = async (
      attemptId: number | null,
    ): Promise<void> => {
      if (attemptId == null || store.postInFlight() || !ownsLaunchAttempt(attemptId)) return;
      try {
        const activeRun = await api.fetchActiveRun();
        if (!ownsLaunchAttempt(attemptId)) return;
        if (!activeRun) {
          const current = store.activeRun();
          patchState(store, {
            awaitingActivationRelease: false,
            activeRun: current && isTerminalRunStatus(current.status) ? current : null,
          });
          clearLaunchAttempt(attemptId);
          return;
        }

        const previousCorrelationId = store.trackedCorrelationId();
        patchState(store, {
          trackedCorrelationId: activeRun.correlationId,
          launchAttemptCorrelationId: activeRun.correlationId,
          activeRun: isSameRunCorrelation(previousCorrelationId, activeRun.correlationId)
            ? store.activeRun()
            : null,
        });
        await applyRunSnapshotAsync(activeRun.correlationId, activeRun);
      } catch {
        // An unknown POST outcome remains busy and is retried by polling.
      }
    };

    const refreshTrackedRunAsync = async (): Promise<void> => {
      const correlationId = store.trackedCorrelationId();
      if (store.postInFlight()) return;
      if (
        store.runLaunchPending() &&
        store.launchAttemptId() != null &&
        !store.launchAttemptCorrelationId()
      ) {
        await reconcileLaunchAttemptAsync(store.launchAttemptId());
        return;
      }
      const attemptId = store.launchAttemptCorrelationId() === correlationId
        ? store.launchAttemptId() ?? undefined
        : undefined;
      if (!correlationId) {
        if (store.runLaunchPending() && store.launchAttemptId() != null) {
          await reconcileLaunchAttemptAsync(store.launchAttemptId());
        }
        return;
      }

      let state: RunStatusInfo | null;
      try {
        state = await api.fetchRunStatus(correlationId);
      } catch {
        await reconcileUnknownRunAsync(correlationId, attemptId);
        return;
      }
      if (!state) {
        await reconcileUnknownRunAsync(correlationId, attemptId);
        return;
      }
      await applyRunSnapshotAsync(correlationId, state);
    };

    const ensurePolling = (): void => {
      if (!browser || pollTimerId !== null) return;
      pollTimerId = window.setInterval(() => void refreshTrackedRunAsync(), POLL_INTERVAL_MS);
    };

    destroyRef.onDestroy(() => stopPolling());

    const adoptCorrelationId = async (
      correlationId: string,
      attemptId?: number,
    ): Promise<void> => {
      const previousCorrelationId = store.trackedCorrelationId();
      patchState(store, {
        trackedCorrelationId: correlationId,
        ...(attemptId != null
          ? { launchAttemptCorrelationId: correlationId }
          : {}),
        activeRun: isSameRunCorrelation(previousCorrelationId, correlationId)
          ? store.activeRun()
          : null,
        awaitingActivationRelease: false,
      });
      await hub.subscribeRun(correlationId);
      try {
        const run = await api.fetchRunStatus(correlationId);
        if (run) {
          await applyRunSnapshotAsync(correlationId, run);
        } else {
          await reconcileUnknownRunAsync(correlationId, attemptId);
        }
      } catch {
        await reconcileUnknownRunAsync(correlationId, attemptId);
      }
    };

    const handleConflict = async (
      error: HttpErrorResponse,
      attemptId?: number,
    ): Promise<boolean> => {
      const details = (error.error ?? {}) as ProblemDetailsResponse;
      if (details.activeCorrelationId) {
        await adoptCorrelationId(details.activeCorrelationId, attemptId);
        return true;
      }
      errorStore.setError(details.detail ?? t('errors.activeRunConflict'));
      return false;
    };

    const isUnknownPostOutcome = (error: unknown): boolean => {
      if (!(error instanceof HttpErrorResponse)) return true;
      return error.status === 0 || error.status >= 500;
    };

    return {
      setPublishRunToGateway(enabled: boolean): void {
        patchState(store, { publishRunToGateway: Boolean(enabled) });
      },

      applyInitialActiveRun(
        payload: RunStatusInfo | { correlationId: string | null } | null,
      ): { correlationId: string | null; status: RunStatusInfo | null } {
        // A manual refresh may overlap an accepted launch. Do not let that
        // bootstrap response erase the local conservative guard while the
        // launch is still being reconciled.
        if (store.runLaunchPending()) {
          return {
            correlationId: store.trackedCorrelationId(),
            status: store.activeRun(),
          };
        }
        const correlationId = payload?.correlationId ?? null;
        patchState(store, {
          trackedCorrelationId: correlationId,
          activeRun: isRunStatusPayload(payload) ? payload : null,
          runEvents: [],
          awaitingActivationRelease:
            isRunStatusPayload(payload) && isTerminalRunStatus(payload.status),
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
        try {
          const run = knownStatus ?? (await api.fetchRunStatus(correlationId));
          if (run) {
            await applyRunSnapshotAsync(correlationId, run);
          } else {
            await reconcileUnknownRunAsync(correlationId);
          }
        } catch {
          await reconcileUnknownRunAsync(correlationId);
        }
      },

      /**
       * @param runAllMembers запустить полную выгрузку по каталогу (главная карточка),
       * не трогая сохранённый выбор мемберов; false — использовать выбор из панели.
       */
      async startRunAsync(runAllMembers = false): Promise<void> {
        // Both the main card and member-selection panel call this method. Set
        // the guard before the first await so a slow POST cannot be duplicated.
        if (store.runLaunchPending() || store.isRunBusy()) return;
        const attemptId = ++nextLaunchAttemptId;
        patchState(store, {
          runLaunchPending: true,
          postInFlight: true,
          launchAttemptId: attemptId,
          launchAttemptCorrelationId: null,
        });
        errorStore.clear();
        patchState(store, { runEvents: [] });

        const targets = catalog.catalog()?.targets ?? [];
        const targetCodes = runAllMembers
          ? targets.map((target) => target.targetCode)
          : selection.selectedTargetCodes();
        const memberCodes = runAllMembers
          ? targets.map((target) => target.memberCode)
          : selection.resolveSelectedMemberCodes(catalog.catalog());

        let keepLaunchAttempt = false;
        try {
          const response = await api.startRun({
            targetCodes,
            memberCodes,
            adminOverride: admin.adminMode(),
            publishToGateway: store.publishRunToGateway(),
          });
          patchState(store, { postInFlight: false });
          await adoptCorrelationId(response.correlationId, attemptId);
        } catch (error) {
          patchState(store, { postInFlight: false });
          if (error instanceof HttpErrorResponse && error.status === 409) {
            keepLaunchAttempt = true;
            const adopted = await handleConflict(error, attemptId);
            if (!adopted) {
              await reconcileLaunchAttemptAsync(attemptId);
            }
            return;
          }
          if (isUnknownPostOutcome(error)) {
            keepLaunchAttempt = true;
            await reconcileLaunchAttemptAsync(attemptId);
            return;
          }
          errorStore.setError(toErrorMessage(error, t('errors.runStartFailed')));
        } finally {
          // Unknown POST outcomes deliberately keep this attempt alive until
          // active-run reconciliation confirms null or adopts a live run.
          if (!keepLaunchAttempt && ownsLaunchAttempt(attemptId)) {
            clearLaunchAttempt(attemptId);
          }
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
        tap((event) => {
          // Игнорируем события чужих запусков (в т.ч. extra) — копим лог только отслеживаемого run.
          if (event.correlationId !== store.trackedCorrelationId()) {
            return;
          }
          patchState(store, (current) => ({
            runEvents: [event, ...current.runEvents].slice(0, RUN_EVENT_LIMIT),
          }));
        }),
      ),

      _trackRunStatus: rxMethod<RunStatusInfo>(
        tap((status) => {
          // Этот стор владеет только main-run; extra трекает ExtraStore.
          if ((status.taskCode ?? '').trim().toLowerCase() !== 'run') {
            return;
          }
          // run_status is broadcast to all clients. Only an explicit bootstrap,
          // POST response, or 409 adoption establishes the correlation we own;
          // never adopt a foreign main run opportunistically.
          const correlationId = store.trackedCorrelationId();
          if (!isSameRunCorrelation(correlationId, status.correlationId)) return;
          if (!shouldAcceptRunStatus(store.activeRun(), status, correlationId)) return;
          patchState(store, {
            activeRun: status,
            ...(isTerminalRunStatus(status.status) ? { awaitingActivationRelease: true } : {}),
          });
          if (isTerminalRunStatus(status.status)) {
            dashboard.markRunCompleted(status);
            void dashboard.refreshTodayRunsAsync();
            void dashboard.refreshDashboardAsync();
            void checkActivationReleaseAsync(status.correlationId);
          }
        }),
      ),

      _trackReconnect: rxMethod<void>(
        tap(() => {
          void hub.subscribeRun(store.trackedCorrelationId());
          void refreshTrackedRunAsync();
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

      // Polling also continues after a terminal event until GET /active confirms
      // that RunActivationChannel has released the slot.
      effect(() => {
        const shouldPoll =
          store.isRunBusy() &&
          !store.postInFlight() &&
          (!!store.trackedCorrelationId() || store.runLaunchPending()) &&
          (!hub.connectionReady() ||
            store.isAwaitingActivationRelease() ||
            store.runLaunchPending());
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
