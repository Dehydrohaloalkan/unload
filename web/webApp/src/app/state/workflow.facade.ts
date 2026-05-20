import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, computed, inject, signal } from '@angular/core';
import { RequeueItem } from '../app.models';
import { AdminStore } from './admin.store';
import { ApiClientService } from './api-client.service';
import { CatalogStore } from './catalog.store';
import { DashboardStore } from './dashboard.store';
import { WorkflowErrorStore } from './error.store';
import { ExtraStore } from './extra.store';
import { OutputFilesStore } from './output-files.store';
import { PresetStore } from './preset.store';
import { RealtimeHubService } from './realtime-hub.service';
import { RunStore } from './run.store';
import { SelectionStore } from './selection.store';
import { ServerClockService } from './server-clock.service';
import { toErrorMessage } from './utils/error-message.util';
import { buildMemberGroups } from './utils/member-projections.util';

/**
 * Фасад поверх signalStore'ов: оркестрирует bootstrap, прокидывает удобные
 * computed-сигналы в шаблоны и реализует кросс-сторовые действия.
 */
@Injectable({ providedIn: 'root' })
export class WorkflowStore {
  private readonly api = inject(ApiClientService);
  private readonly hub = inject(RealtimeHubService);
  private readonly clock = inject(ServerClockService);
  private readonly catalogStore = inject(CatalogStore);
  private readonly selectionStore = inject(SelectionStore);
  private readonly outputFilesStore = inject(OutputFilesStore);
  private readonly adminStore = inject(AdminStore);
  private readonly presetStore = inject(PresetStore);
  private readonly extraStore = inject(ExtraStore);
  private readonly runStore = inject(RunStore);
  private readonly dashboardStore = inject(DashboardStore);
  private readonly errorStore = inject(WorkflowErrorStore);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly browser = isPlatformBrowser(this.platformId);

  private initialized = false;

  readonly loading = signal(true);
  readonly ready = signal(false);

  readonly errorMessage = this.errorStore.errorMessage;
  readonly connectionReady = this.hub.connectionReady;
  readonly currentTime = this.clock.currentTime;
  readonly serverTimeZoneId = this.clock.timeZoneId;

  readonly catalog = this.catalogStore.catalog;
  readonly members = this.catalogStore.members;
  readonly historyMemberNames = this.catalogStore.historyMemberNames;

  readonly selectedTargetCodes = this.selectionStore.selectedTargetCodes;
  readonly selectedCount = this.selectionStore.selectedCount;

  readonly outputFilesByPath = this.outputFilesStore.filesByOutputPath;

  readonly adminMode = this.adminStore.adminMode;

  readonly presetState = this.presetStore.presetState;
  readonly presetTask = this.presetStore.presetTask;
  readonly extraTask = this.extraStore.extraTask;
  readonly publishExtraToGateway = this.extraStore.publishExtraToGateway;

  readonly activeRun = this.runStore.activeRun;
  readonly trackedCorrelationId = this.runStore.trackedCorrelationId;
  readonly runEvents = this.runStore.runEvents;
  readonly publishRunToGateway = this.runStore.publishRunToGateway;
  readonly requeueRunning = this.runStore.requeueRunning;
  readonly requeueResult = this.runStore.requeueResult;
  readonly isRunBusy = this.runStore.isRunBusy;

  readonly hasRunToday = this.dashboardStore.hasRunToday;
  readonly hasExtraToday = this.dashboardStore.hasExtraToday;
  readonly runLastCompletedAt = this.dashboardStore.runLastCompletedAt;
  readonly extraLastCompletedAt = this.dashboardStore.extraLastCompletedAt;
  readonly todayHistory = this.dashboardStore.todayHistory;
  readonly todayRuns = this.dashboardStore.todayRuns;
  readonly allTodayRuns = this.dashboardStore.allTodayRuns;
  readonly latestTodayRun = this.dashboardStore.latestTodayRun;

  readonly buildSystemDownloadUrl = this.api.buildDownloadUrl;
  readonly buildSystemArchiveUrl = this.api.buildArchiveUrl;

  readonly memberGroups = computed(() =>
    buildMemberGroups(
      this.catalogStore.catalog(),
      this.catalogStore.members(),
      this.runStore.activeRun(),
      this.runStore.runEvents(),
      this.selectionStore.selectedTargetCodes(),
    ),
  );

  readonly canRunPreset = computed(() => this.presetStore.canRunPreset(this.runStore.isRunBusy()));

  readonly canRunMainOrExtra = computed(() => {
    const preset = this.presetStore.presetState();
    if (!preset) {
      return false;
    }
    return !preset.requiresPresetExecution || preset.presetCompleted;
  });

  readonly canStartRun = computed(
    () =>
      this.selectionStore.selectedCount() > 0 &&
      (this.canRunMainOrExtra() || this.adminStore.adminMode()) &&
      !this.runStore.isRunBusy(),
  );

  readonly canRunExtra = computed(
    () =>
      (this.canRunMainOrExtra() || this.adminStore.adminMode()) &&
      !this.extraStore.extraTask().running,
  );

  readonly phase = computed<'gate' | 'tasks'>(() =>
    this.presetStore.presetState()?.presetCompleted ? 'tasks' : 'gate',
  );

  init(): void {
    if (this.initialized) {
      return;
    }
    this.initialized = true;

    if (this.browser) {
      this.clock.init();
      this.presetStore.restore();
      this.extraStore.restore();
    }

    void this.hub.connect();
    void this.bootstrapAsync();
  }

  async refreshAsync(): Promise<void> {
    await this.bootstrapAsync();
  }

  toggleMember(targetCodes: string[], selected: boolean): void {
    this.selectionStore.toggleMember(targetCodes, selected);
  }

  selectAllMembers(): void {
    const codes = this.catalogStore.catalog()?.targets.map((target) => target.targetCode) ?? [];
    this.selectionStore.selectAll(codes);
  }

  clearMemberSelection(): void {
    this.selectionStore.clearAll();
  }

  setPublishRunToGateway(enabled: boolean): void {
    this.runStore.setPublishRunToGateway(enabled);
  }

  setPublishExtraToGateway(enabled: boolean): void {
    this.extraStore.setPublishExtraToGateway(enabled);
  }

  setAdminMode(enabled: boolean): void {
    this.adminStore.setAdminMode(enabled);
  }

  async runPresetAsync(): Promise<void> {
    try {
      await this.presetStore.runPresetAsync(this.adminStore.adminMode());
    } catch {
      return;
    }
    await this.dashboardStore.refreshDashboardAsync();
  }

  async runExtraAsync(): Promise<void> {
    try {
      await this.extraStore.runExtraAsync();
    } catch {
      return;
    }
    await this.dashboardStore.refreshDashboardAsync();
  }

  startRunAsync(): Promise<void> {
    return this.runStore.startRunAsync();
  }

  stopRunAsync(): Promise<void> {
    return this.runStore.stopRunAsync();
  }

  requeueToGatewayAsync(items: RequeueItem[]): Promise<void> {
    return this.runStore.requeueToGatewayAsync(items);
  }

  private async bootstrapAsync(): Promise<void> {
    this.loading.set(true);
    this.errorStore.clear();

    try {
      const [catalog, members, presetState, activeRunPayload, serverTime, dashboard, runsToday] =
        await Promise.all([
          this.api.fetchCatalog(),
          this.api.fetchMembers(),
          this.api.fetchPresetState(),
          this.api.fetchActiveRun(),
          this.api.fetchServerTime(),
          this.api.fetchDashboardSnapshot(),
          this.api.fetchTodayRuns(),
        ]);

      this.clock.applyFromResponse(serverTime.serverLocalTime, serverTime.timeZoneId);
      this.catalogStore.setCatalog(catalog);
      this.catalogStore.setMembers(members);
      this.presetStore.setPresetState(dashboard.presetState ?? presetState);
      this.dashboardStore.applySnapshot(dashboard);
      this.dashboardStore.applyTodayRuns(runsToday);
      await this.outputFilesStore.refreshForHistory(dashboard.todayHistory ?? []);
      this.selectionStore.reconcileFromCatalog(catalog);

      const { correlationId, status } = this.runStore.applyInitialActiveRun(activeRunPayload);
      if (correlationId) {
        await this.runStore.syncActiveRunAsync(correlationId, status);
      }

      this.ready.set(true);
    } catch (error) {
      this.errorStore.setError(
        toErrorMessage(error, 'Не удалось загрузить состояние приложения.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
