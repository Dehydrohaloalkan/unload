import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { PresetGateState, TaskUiState } from '../app.models';
import { AdminStore } from './admin.store';
import { ApiClientService } from './api-client.service';
import { WorkflowErrorStore } from './error.store';
import { browserStorage, readJson, writeJson } from './utils/storage.util';
import {
  createIdleTaskState,
  normalizeRestoredTaskState,
} from './utils/task-state.util';
import { toErrorMessage } from './utils/error-message.util';

const PRESET_TASK_STORAGE_KEY = 'unload.web.preset-task';

@Injectable({ providedIn: 'root' })
export class PresetStore {
  private readonly api = inject(ApiClientService);
  private readonly admin = inject(AdminStore);
  private readonly errorStore = inject(WorkflowErrorStore);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly browser = isPlatformBrowser(this.platformId);
  private readonly storage = browserStorage(this.browser);

  readonly presetState = signal<PresetGateState | null>(null);
  readonly presetTask = signal<TaskUiState>(createIdleTaskState());

  readonly presetCompletedAt = computed(() => {
    const task = this.presetTask();
    return task.result && task.completedAt ? task.completedAt : null;
  });

  restore(): void {
    const payload = readJson<Partial<TaskUiState>>(this.storage, PRESET_TASK_STORAGE_KEY);
    this.presetTask.set(normalizeRestoredTaskState(payload));
  }

  setPresetState(state: PresetGateState | null): void {
    this.presetState.set(state);
  }

  canRunPreset(isRunBusy: boolean): boolean {
    const preset = this.presetState();
    return (
      !!preset &&
      preset.readyForPreset &&
      !preset.presetCompleted &&
      !this.presetTask().running &&
      !isRunBusy
    );
  }

  async refreshState(): Promise<void> {
    const state = await this.api.fetchPresetState();
    this.presetState.set(state);
  }

  async runPresetAsync(adminMode: boolean): Promise<void> {
    this.errorStore.clear();
    const startedAt = new Date().toISOString();
    this.updateTask({
      running: true,
      startedAt,
      completedAt: null,
      result: null,
      error: null,
      stale: false,
    });

    try {
      const result = await this.api.runPreset(adminMode || this.admin.adminMode());
      this.updateTask({
        running: false,
        startedAt,
        completedAt: new Date().toISOString(),
        result,
        error: null,
        stale: false,
      });
      await this.refreshState();
    } catch (error) {
      const message = toErrorMessage(error, 'Не удалось запустить preset-задачу.');
      this.updateTask({
        running: false,
        startedAt,
        completedAt: new Date().toISOString(),
        result: null,
        error: message,
        stale: false,
      });
      this.errorStore.setError(message);
      throw error;
    }
  }

  private updateTask(state: TaskUiState): void {
    this.presetTask.set(state);
    writeJson(this.storage, PRESET_TASK_STORAGE_KEY, state);
  }
}
