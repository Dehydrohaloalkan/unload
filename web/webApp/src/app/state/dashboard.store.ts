import { Injectable, inject, isDevMode, signal } from '@angular/core';
import {
  RunLifecycleStatus,
  RunStatusInfo,
  TaskRecord,
  WorkflowDashboardSnapshotResponse,
} from '../app.models';
import { ApiClientService } from './api-client.service';
import { OutputFilesStore } from './output-files.store';
import { PresetStore } from './preset.store';
import { isMainRunHistoryEntry } from './utils/run-status.util';

@Injectable({ providedIn: 'root' })
export class DashboardStore {
  private readonly api = inject(ApiClientService);
  private readonly outputFiles = inject(OutputFilesStore);
  private readonly preset = inject(PresetStore);

  readonly hasRunToday = signal(false);
  readonly hasExtraToday = signal(false);
  readonly runLastCompletedAt = signal<string | null>(null);
  readonly extraLastCompletedAt = signal<string | null>(null);
  readonly todayHistory = signal<TaskRecord[]>([]);
  readonly todayRuns = signal<RunStatusInfo[]>([]);
  /** All runs returned by the API today, unfiltered (includes extra runs). */
  readonly allTodayRuns = signal<RunStatusInfo[]>([]);

  applySnapshot(snapshot: WorkflowDashboardSnapshotResponse): void {
    this.hasRunToday.set(Boolean(snapshot.hasRunToday));
    this.hasExtraToday.set(Boolean(snapshot.hasExtraToday));
    this.runLastCompletedAt.set(snapshot.runLastCompletedAt ?? null);
    this.extraLastCompletedAt.set(snapshot.extraLastCompletedAt ?? null);
    this.todayHistory.set(snapshot.todayHistory ?? []);

    const nextPresetState = snapshot.presetState ?? this.preset.presetState();
    this.preset.setPresetState(nextPresetState);
  }

  applyTodayRuns(runs: RunStatusInfo[]): void {
    this.allTodayRuns.set(runs ?? []);
    const filtered = runs.filter(isMainRunHistoryEntry);
    this.todayRuns.set(filtered);
    this.recalculateFromRuns(filtered);
  }

  markRunCompleted(run: RunStatusInfo): void {
    this.hasRunToday.set(true);
    if (run.status === RunLifecycleStatus.Completed) {
      this.runLastCompletedAt.set(run.updatedAt);
    }
  }

  async refreshDashboardAsync(): Promise<void> {
    try {
      const snapshot = await this.api.fetchDashboardSnapshot();
      this.applySnapshot(snapshot);
      await this.refreshTodayRunsAsync();
      await this.outputFiles.refreshForHistory(snapshot.todayHistory ?? []);
    } catch (error) {
      if (isDevMode()) {
        console.error(error);
      }
    }
  }

  async refreshTodayRunsAsync(): Promise<void> {
    try {
      const runs = await this.api.fetchTodayRuns();
      this.applyTodayRuns(runs);
    } catch (error) {
      if (isDevMode()) {
        console.error(error);
      }
    }
  }

  private recalculateFromRuns(runs: RunStatusInfo[]): void {
    const sorted = [...runs].sort(
      (left, right) => new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime(),
    );

    this.hasRunToday.set(sorted.length > 0);
    const completedRun = sorted.find((run) => run.status === RunLifecycleStatus.Completed) ?? null;
    this.runLastCompletedAt.set(completedRun?.updatedAt ?? null);
  }
}
