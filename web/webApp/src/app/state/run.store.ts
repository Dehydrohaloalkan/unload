import { isPlatformBrowser } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  DestroyRef,
  Injectable,
  PLATFORM_ID,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  ProblemDetailsResponse,
  RequeueItem,
  RequeueToMqResponse,
  RunStatusInfo,
  RunnerEvent,
} from '../app.models';
import { AdminStore } from './admin.store';
import { ApiClientService } from './api-client.service';
import { CatalogStore } from './catalog.store';
import { DashboardStore } from './dashboard.store';
import { WorkflowErrorStore } from './error.store';
import { RealtimeHubService } from './realtime-hub.service';
import { SelectionStore } from './selection.store';
import { toErrorMessage } from './utils/error-message.util';
import {
  isRunStatusPayload,
  isTerminalRunStatus,
} from './utils/run-status.util';

const RUN_EVENT_LIMIT = 80;
const POLL_INTERVAL_MS = 2500;

@Injectable({ providedIn: 'root' })
export class RunStore {
  private readonly api = inject(ApiClientService);
  private readonly catalog = inject(CatalogStore);
  private readonly selection = inject(SelectionStore);
  private readonly dashboard = inject(DashboardStore);
  private readonly admin = inject(AdminStore);
  private readonly errorStore = inject(WorkflowErrorStore);
  private readonly hub = inject(RealtimeHubService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly browser = isPlatformBrowser(this.platformId);

  readonly activeRun = signal<RunStatusInfo | null>(null);
  readonly trackedCorrelationId = signal<string | null>(null);
  readonly runEvents = signal<RunnerEvent[]>([]);
  readonly publishRunToMq = signal(true);
  readonly requeueRunning = signal(false);
  readonly requeueResult = signal<RequeueToMqResponse | null>(null);

  readonly isRunBusy = computed(() => {
    const run = this.activeRun();
    return !!run && !isTerminalRunStatus(run.status);
  });

  private pollTimerId: number | null = null;

  init(): void {
    this.hub.statusEvents$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((event) => this.onStatusEvent(event));

    this.hub.runStatusEvents$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((status) => this.onRunStatus(status));

    this.hub.reconnected$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => void this.hub.subscribeRun(this.trackedCorrelationId()));

    this.destroyRef.onDestroy(() => this.stopPolling());
  }

  setPublishRunToMq(enabled: boolean): void {
    this.publishRunToMq.set(Boolean(enabled));
  }

  applyInitialActiveRun(
    payload: RunStatusInfo | { correlationId: string | null } | null,
  ): { correlationId: string | null; status: RunStatusInfo | null } {
    const correlationId = payload?.correlationId ?? null;
    this.trackedCorrelationId.set(correlationId);
    this.activeRun.set(null);
    this.runEvents.set([]);
    return {
      correlationId,
      status: isRunStatusPayload(payload) ? payload : null,
    };
  }

  async syncActiveRunAsync(correlationId: string, knownStatus: RunStatusInfo | null): Promise<void> {
    await this.hub.subscribeRun(correlationId);
    const run = knownStatus ?? (await this.api.fetchRunStatus(correlationId));
    if (!run) {
      this.stopPolling();
      return;
    }

    this.activeRun.set(run);
    if (isTerminalRunStatus(run.status)) {
      this.stopPolling();
    } else {
      this.ensurePolling();
    }
  }

  async startRunAsync(): Promise<void> {
    this.errorStore.clear();
    this.runEvents.set([]);

    try {
      const response = await this.api.startRun({
        targetCodes: this.selection.selectedTargetCodes(),
        memberCodes: this.selection.resolveSelectedMemberCodes(this.catalog.catalog()),
        adminOverride: this.admin.adminMode(),
        publishToMq: this.publishRunToMq(),
      });

      await this.adoptCorrelationId(response.correlationId);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        await this.handleConflict(error);
        return;
      }
      this.errorStore.setError(toErrorMessage(error, 'Не удалось запустить run.'));
    }
  }

  async stopRunAsync(): Promise<void> {
    const correlationId = this.trackedCorrelationId();
    if (!correlationId || !this.isRunBusy()) {
      return;
    }

    this.errorStore.clear();
    try {
      await this.api.stopRun(correlationId);
    } catch (error) {
      this.errorStore.setError(
        toErrorMessage(error, 'Не удалось отправить запрос на остановку.'),
      );
    }
  }

  async requeueToMqAsync(items: RequeueItem[]): Promise<void> {
    if (!items || items.length === 0) {
      return;
    }

    this.errorStore.clear();
    this.requeueRunning.set(true);
    this.requeueResult.set(null);
    try {
      const response = await this.api.requeueToMq(items);
      this.requeueResult.set(response ?? null);
      await this.dashboard.refreshDashboardAsync();
    } catch (error) {
      this.errorStore.setError(toErrorMessage(error, 'Не удалось отправить выбранное в MQ.'));
    } finally {
      this.requeueRunning.set(false);
    }
  }

  private async adoptCorrelationId(correlationId: string): Promise<void> {
    this.trackedCorrelationId.set(correlationId);
    await this.hub.subscribeRun(correlationId);
    const run = await this.api.fetchRunStatus(correlationId);
    if (run) {
      this.activeRun.set(run);
      this.ensurePolling();
    }
  }

  private async handleConflict(error: HttpErrorResponse): Promise<void> {
    const details = (error.error ?? {}) as ProblemDetailsResponse;
    const activeCorrelationId = details.activeCorrelationId;
    if (activeCorrelationId) {
      await this.adoptCorrelationId(activeCorrelationId);
    }
    this.errorStore.setError(
      details.detail ?? 'Уже выполняется другой run. Переключаюсь в режим наблюдения.',
    );
  }

  private onStatusEvent(event: RunnerEvent): void {
    this.runEvents.update((current) => [event, ...current].slice(0, RUN_EVENT_LIMIT));
  }

  private onRunStatus(status: RunStatusInfo): void {
    this.trackedCorrelationId.set(status.correlationId);
    this.activeRun.set(status);
    if (isTerminalRunStatus(status.status)) {
      this.dashboard.markRunCompleted(status);
      this.stopPolling();
      void this.dashboard.refreshTodayRunsAsync();
      void this.dashboard.refreshDashboardAsync();
    } else {
      this.ensurePolling();
    }
  }

  private ensurePolling(): void {
    if (!this.browser || this.pollTimerId !== null) {
      return;
    }

    this.pollTimerId = window.setInterval(() => {
      void this.refreshTrackedRunAsync();
    }, POLL_INTERVAL_MS);
  }

  private stopPolling(): void {
    if (!this.browser || this.pollTimerId === null) {
      return;
    }

    window.clearInterval(this.pollTimerId);
    this.pollTimerId = null;
  }

  private async refreshTrackedRunAsync(): Promise<void> {
    const correlationId = this.trackedCorrelationId();
    if (!correlationId) {
      this.stopPolling();
      return;
    }

    const state = await this.api.fetchRunStatus(correlationId);
    if (!state) {
      this.stopPolling();
      this.trackedCorrelationId.set(null);
      this.activeRun.set(null);
      return;
    }

    this.activeRun.set(state);
    if (isTerminalRunStatus(state.status)) {
      this.stopPolling();
      await this.dashboard.refreshTodayRunsAsync();
    }
  }
}
