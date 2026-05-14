import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';
import { TaskUiState } from '../app.models';
import { AdminStore } from './admin.store';
import { ApiClientService } from './api-client.service';
import { WorkflowErrorStore } from './error.store';
import { browserStorage, readJson, writeJson } from './utils/storage.util';
import {
  createIdleTaskState,
  normalizeRestoredTaskState,
} from './utils/task-state.util';
import { toErrorMessage } from './utils/error-message.util';

const EXTRA_TASK_STORAGE_KEY = 'unload.web.extra-task';

@Injectable({ providedIn: 'root' })
export class ExtraStore {
  private readonly api = inject(ApiClientService);
  private readonly admin = inject(AdminStore);
  private readonly errorStore = inject(WorkflowErrorStore);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly browser = isPlatformBrowser(this.platformId);
  private readonly storage = browserStorage(this.browser);

  readonly extraTask = signal<TaskUiState>(createIdleTaskState());
  readonly publishExtraToGateway = signal(true);

  restore(): void {
    const payload = readJson<Partial<TaskUiState>>(this.storage, EXTRA_TASK_STORAGE_KEY);
    this.extraTask.set(normalizeRestoredTaskState(payload));
  }

  setPublishExtraToGateway(enabled: boolean): void {
    this.publishExtraToGateway.set(Boolean(enabled));
  }

  async runExtraAsync(): Promise<void> {
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
      const result = await this.api.runExtra(this.admin.adminMode(), this.publishExtraToGateway());
      this.updateTask({
        running: false,
        startedAt,
        completedAt: new Date().toISOString(),
        result,
        error: null,
        stale: false,
      });
    } catch (error) {
      const message = toErrorMessage(error, 'Не удалось запустить extra-задачу.');
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
    this.extraTask.set(state);
    writeJson(this.storage, EXTRA_TASK_STORAGE_KEY, state);
  }
}
