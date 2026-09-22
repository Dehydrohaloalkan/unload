import { computed, inject, isDevMode } from '@angular/core';
import {
  signalStore,
  withComputed,
  withMethods,
  withState,
  patchState,
} from '@ngrx/signals';
import {
  RunLifecycleStatus,
  RunStatusInfo,
  TaskRecord,
  WorkflowDashboardSnapshotResponse,
} from '../app.models';
import { ApiClientService } from './api-client.service';
import { OutputFilesStore } from './output-files.store';
import { PresetStore } from './preset.store';
import { byDescDate } from './utils/compare.util';
import { newerTimestamp } from './utils/run-lifecycle.util';
import { isExtraRunEntry, isMainRunHistoryEntry } from './utils/run-status.util';

interface DashboardState {
  hasRunToday: boolean;
  hasExtraToday: boolean;
  runLastCompletedAt: string | null;
  extraLastCompletedAt: string | null;
  todayHistory: TaskRecord[];
  todayRuns: RunStatusInfo[];
  /** Все run-ы за сегодня (включая extra), как пришли с API. */
  allTodayRuns: RunStatusInfo[];
}

const INITIAL: DashboardState = {
  hasRunToday: false,
  hasExtraToday: false,
  runLastCompletedAt: null,
  extraLastCompletedAt: null,
  todayHistory: [],
  todayRuns: [],
  allTodayRuns: [],
};

export const DashboardStore = signalStore(
  { providedIn: 'root' },
  withState(INITIAL),
  withComputed(({ todayRuns }) => ({
    /** Самый свежий main-run за сегодня. */
    latestTodayRun: computed<RunStatusInfo | null>(() => {
      const runs = todayRuns();
      if (runs.length === 0) {
        return null;
      }
      return [...runs].sort(byDescDate<RunStatusInfo>((run) => run.createdAt))[0] ?? null;
    }),
  })),
  withMethods((store) => {
    const api = inject(ApiClientService);
    const outputFiles = inject(OutputFilesStore);
    const preset = inject(PresetStore);
    let serverDayEpoch = 0;

    const recalculateFromRuns = (runs: RunStatusInfo[]): void => {
      const sorted = [...runs].sort(byDescDate<RunStatusInfo>((run) => run.createdAt));
      const completedRun =
        sorted.find((run) => run.status === RunLifecycleStatus.Completed) ?? null;
      patchState(store, {
        // A terminal run can arrive over SignalR before history/dashboard
        // persistence catches up. Never let that accepted local fact disappear
        // because a stale empty response arrived later in the same server day.
        hasRunToday: store.hasRunToday() || sorted.length > 0,
        runLastCompletedAt: newerTimestamp(store.runLastCompletedAt(), completedRun?.updatedAt),
      });
    };

    // Снапшот дашборда строится из истории, которую бекенд пишет с задержкой после терминального
    // статуса, — флаг «extra уже была» выводим и из сегодняшних запусков, чтобы кнопка гасла сразу.
    const recalculateExtraFromRuns = (runs: RunStatusInfo[]): void => {
      const completedExtra = runs
        .filter((run) => isExtraRunEntry(run) && run.status === RunLifecycleStatus.Completed)
        .sort(byDescDate<RunStatusInfo>((run) => run.updatedAt))[0];
      if (completedExtra) {
        patchState(store, {
          hasExtraToday: true,
          extraLastCompletedAt: newerTimestamp(
            store.extraLastCompletedAt(),
            completedExtra.updatedAt,
          ),
        });
      }
    };

    return {
      applySnapshot(snapshot: WorkflowDashboardSnapshotResponse, epoch = serverDayEpoch): void {
        if (epoch !== serverDayEpoch) return;
        patchState(store, {
          hasRunToday: store.hasRunToday() || Boolean(snapshot.hasRunToday),
          hasExtraToday: store.hasExtraToday() || Boolean(snapshot.hasExtraToday),
          runLastCompletedAt: newerTimestamp(store.runLastCompletedAt(), snapshot.runLastCompletedAt),
          extraLastCompletedAt: newerTimestamp(
            store.extraLastCompletedAt(),
            snapshot.extraLastCompletedAt,
          ),
          todayHistory: snapshot.todayHistory ?? [],
        });
        const nextPresetState = snapshot.presetState ?? preset.presetState();
        preset.setPresetState(nextPresetState);
      },

      applyTodayRuns(runs: RunStatusInfo[], epoch = serverDayEpoch): void {
        if (epoch !== serverDayEpoch) return;
        const all = runs ?? [];
        const filtered = all.filter(isMainRunHistoryEntry);
        patchState(store, { allTodayRuns: all, todayRuns: filtered });
        recalculateFromRuns(filtered);
        recalculateExtraFromRuns(all);
      },

      markRunCompleted(run: RunStatusInfo): void {
        patchState(store, {
          hasRunToday: true,
          ...(run.status === RunLifecycleStatus.Completed
            ? { runLastCompletedAt: newerTimestamp(store.runLastCompletedAt(), run.updatedAt) }
            : {}),
        });
      },

      resetForNewServerDay(): void {
        serverDayEpoch += 1;
        patchState(store, {
          hasRunToday: false,
          hasExtraToday: false,
          runLastCompletedAt: null,
          extraLastCompletedAt: null,
          todayHistory: [],
          todayRuns: [],
          allTodayRuns: [],
        });
      },

      async refreshDashboardAsync(): Promise<void> {
        const epoch = serverDayEpoch;
        try {
          const snapshot = await api.fetchDashboardSnapshot();
          if (epoch !== serverDayEpoch) return;
          this.applySnapshot(snapshot, epoch);
          await this.refreshTodayRunsAsync(epoch);
          if (epoch !== serverDayEpoch) return;
          await outputFiles.refreshForHistory(snapshot.todayHistory ?? []);
        } catch (error) {
          if (isDevMode()) {
            console.error(error);
          }
        }
      },

      async refreshTodayRunsAsync(epoch = serverDayEpoch): Promise<void> {
        try {
          const runs = await api.fetchTodayRuns();
          this.applyTodayRuns(runs, epoch);
        } catch (error) {
          if (isDevMode()) {
            console.error(error);
          }
        }
      },
    };
  }),
);

export type DashboardStore = InstanceType<typeof DashboardStore>;
