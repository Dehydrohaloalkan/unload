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
import { isMainRunHistoryEntry } from './utils/run-status.util';

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

    const recalculateFromRuns = (runs: RunStatusInfo[]): void => {
      const sorted = [...runs].sort(byDescDate<RunStatusInfo>((run) => run.createdAt));
      const completedRun =
        sorted.find((run) => run.status === RunLifecycleStatus.Completed) ?? null;
      patchState(store, {
        hasRunToday: sorted.length > 0,
        runLastCompletedAt: completedRun?.updatedAt ?? null,
      });
    };

    return {
      applySnapshot(snapshot: WorkflowDashboardSnapshotResponse): void {
        patchState(store, {
          hasRunToday: Boolean(snapshot.hasRunToday),
          hasExtraToday: Boolean(snapshot.hasExtraToday),
          runLastCompletedAt: snapshot.runLastCompletedAt ?? null,
          extraLastCompletedAt: snapshot.extraLastCompletedAt ?? null,
          todayHistory: snapshot.todayHistory ?? [],
        });
        const nextPresetState = snapshot.presetState ?? preset.presetState();
        preset.setPresetState(nextPresetState);
      },

      applyTodayRuns(runs: RunStatusInfo[]): void {
        const all = runs ?? [];
        const filtered = all.filter(isMainRunHistoryEntry);
        patchState(store, { allTodayRuns: all, todayRuns: filtered });
        recalculateFromRuns(filtered);
      },

      markRunCompleted(run: RunStatusInfo): void {
        patchState(store, {
          hasRunToday: true,
          ...(run.status === RunLifecycleStatus.Completed
            ? { runLastCompletedAt: run.updatedAt }
            : {}),
        });
      },

      async refreshDashboardAsync(): Promise<void> {
        try {
          const snapshot = await api.fetchDashboardSnapshot();
          this.applySnapshot(snapshot);
          await this.refreshTodayRunsAsync();
          await outputFiles.refreshForHistory(snapshot.todayHistory ?? []);
        } catch (error) {
          if (isDevMode()) {
            console.error(error);
          }
        }
      },

      async refreshTodayRunsAsync(): Promise<void> {
        try {
          const runs = await api.fetchTodayRuns();
          this.applyTodayRuns(runs);
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
