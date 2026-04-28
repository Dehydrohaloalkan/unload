import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { PLATFORM_ID, computed, inject, Injectable, isDevMode, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
} from '@microsoft/signalr';
import {
  CatalogInfo,
  MemberCatalogItem,
  MemberGroupViewModel,
  MemberLogLine,
  MemberRunLifecycleStatus,
  MemberRunStatusInfo,
  MemberViewModel,
  PresetGateState,
  ProblemDetailsResponse,
  OutputFileInfo,
  RunnerEvent,
  RunnerStep,
  RunAcceptedResponse,
  RunLifecycleStatus,
  RunOutputArtifactInfo,
  RunStatusInfo,
  ServerTimeResponse,
  ScriptTaskRunResult,
  TaskRecord,
  WorkflowDashboardSnapshotResponse,
  TaskUiState,
} from './app.models';

interface ActiveRunPayload {
  correlationId: string | null;
  status?: RunLifecycleStatus;
  createdAt?: string;
}

const MEMBER_SELECTION_STORAGE_KEY = 'unload.web.member-selection';
const EXTRA_TASK_STORAGE_KEY = 'unload.web.extra-task';
const PRESET_TASK_STORAGE_KEY = 'unload.web.preset-task';
const RUN_EVENT_LIMIT = 80;

@Injectable({ providedIn: 'root' })
export class WorkflowStore {
  private readonly http = inject(HttpClient);
  private readonly platformId = inject(PLATFORM_ID);
  private readonly browser = isPlatformBrowser(this.platformId);
  private readonly apiBaseUrl = resolveApiBaseUrl();
  readonly buildSystemDownloadUrl = (path: string): string =>
    this.apiUrl(`/api/system/download?path=${encodeURIComponent(path)}`);
  readonly buildSystemArchiveUrl = (path: string): string =>
    this.apiUrl(`/api/system/download-archive?path=${encodeURIComponent(path)}`);

  private connection: HubConnection | null = null;
  private initialized = false;
  private clockTimerId: number | null = null;
  private timeSyncTimerId: number | null = null;
  private runPollTimerId: number | null = null;
  private serverTimeOffsetMs = 0;

  readonly loading = signal(true);
  readonly ready = signal(false);
  readonly connectionReady = signal(false);
  readonly currentTime = signal(new Date());
  readonly serverTimeZoneId = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);
  readonly catalog = signal<CatalogInfo | null>(null);
  readonly members = signal<MemberCatalogItem[]>([]);
  readonly presetState = signal<PresetGateState | null>(null);
  readonly activeRun = signal<RunStatusInfo | null>(null);
  readonly trackedCorrelationId = signal<string | null>(null);
  readonly runEvents = signal<RunnerEvent[]>([]);
  readonly selectedMemberCodes = signal<string[]>([]);
  readonly presetTask = signal<TaskUiState>(createIdleTaskState());
  readonly extraTask = signal<TaskUiState>(createIdleTaskState());
  readonly hasRunToday = signal(false);
  readonly hasExtraToday = signal(false);
  readonly runLastCompletedAt = signal<string | null>(null);
  readonly extraLastCompletedAt = signal<string | null>(null);
  readonly todayHistory = signal<TaskRecord[]>([]);
  readonly todayRuns = signal<RunStatusInfo[]>([]);
  readonly outputFilesByPath = signal<Record<string, OutputFileInfo[]>>({});
  readonly adminMode = signal(false);

  readonly phase = computed<'gate' | 'tasks'>(() =>
    this.presetState()?.presetCompleted ? 'tasks' : 'gate',
  );

  readonly selectedCount = computed(() => this.selectedMemberCodes().length);

  readonly isRunBusy = computed(() => {
    const run = this.activeRun();
    return !!run && !isTerminalRunStatus(run.status);
  });

  readonly canRunPreset = computed(() => {
    const preset = this.presetState();
    return (
      !!preset &&
      preset.readyForPreset &&
      !preset.presetCompleted &&
      !this.presetTask().running &&
      !this.isRunBusy()
    );
  });

  readonly canRunMainOrExtra = computed(() => {
    const preset = this.presetState();
    if (!preset) {
      return false;
    }

    return !preset.requiresPresetExecution || preset.presetCompleted;
  });

  readonly canStartRun = computed(
    () =>
      this.selectedCount() > 0 &&
      (this.canRunMainOrExtra() || this.adminMode()) &&
      !this.isRunBusy(),
  );

  readonly canRunExtra = computed(
    () => (this.canRunMainOrExtra() || this.adminMode()) && !this.extraTask().running,
  );

  readonly memberGroups = computed(() =>
    buildMemberGroups(
      this.catalog(),
      this.members(),
      this.activeRun(),
      this.runEvents(),
      this.selectedMemberCodes(),
    ),
  );

  init(): void {
    if (this.initialized) {
      return;
    }

    this.initialized = true;

    if (this.browser) {
      this.updateCurrentTime();
      this.clockTimerId = window.setInterval(() => {
        this.updateCurrentTime();
      }, 1000);
      this.timeSyncTimerId = window.setInterval(() => {
        void this.syncServerTimeAsync();
      }, 30000);
      this.restorePresetTaskState();
      this.restoreExtraTaskState();
    }

    void this.connectRealtimeAsync();
    void this.bootstrapAsync();
  }

  toggleMember(code: string, selected: boolean): void {
    const next = new Set<string>(this.selectedMemberCodes());
    if (selected) {
      next.add(code);
    } else {
      next.delete(code);
    }

    this.selectedMemberCodes.set(sortCodes(next));
    this.persistSelection();
  }

  selectAllMembers(): void {
    const codes = this.members().map((member: MemberCatalogItem) => member.code);
    this.selectedMemberCodes.set(sortCodes(codes));
    this.persistSelection();
  }

  clearMemberSelection(): void {
    this.selectedMemberCodes.set([]);
    this.persistSelection();
  }

  async refreshAsync(): Promise<void> {
    await this.bootstrapAsync();
  }

  async runPresetAsync(): Promise<void> {
    if (!this.canRunPreset() && !this.adminMode()) {
      return;
    }

    this.errorMessage.set(null);
    const startedAt = new Date().toISOString();
    this.presetTask.set({
      running: true,
      startedAt,
      completedAt: null,
      result: null,
      error: null,
      stale: false,
    });
    this.persistPresetTask(this.presetTask());

    try {
      const result = await firstValueFrom(
        this.http.post<ScriptTaskRunResult>(this.apiUrl('/api/runs/preset'), {
          adminOverride: this.adminMode(),
        }),
      );
      this.presetTask.set({
        running: false,
        startedAt,
        completedAt: new Date().toISOString(),
        result,
        error: null,
        stale: false,
      });
      this.persistPresetTask(this.presetTask());
      await this.refreshPresetStateAsync();
      await this.refreshDashboardSnapshotAsync();
    } catch (error) {
      const message = this.toErrorMessage(error, 'Не удалось запустить preset-задачу.');
      this.presetTask.set({
        running: false,
        startedAt,
        completedAt: new Date().toISOString(),
        result: null,
        error: message,
        stale: false,
      });
      this.persistPresetTask(this.presetTask());
      this.errorMessage.set(message);
    }
  }

  async runExtraAsync(): Promise<void> {
    if (!this.canRunExtra()) {
      return;
    }

    this.errorMessage.set(null);
    const startedAt = new Date().toISOString();
    const taskState: TaskUiState = {
      running: true,
      startedAt,
      completedAt: null,
      result: null,
      error: null,
      stale: false,
    };

    this.extraTask.set(taskState);
    this.persistExtraTask(taskState);

    try {
      const result = await firstValueFrom(
        this.http.post<ScriptTaskRunResult>(this.apiUrl('/api/runs/extra'), {
          adminOverride: this.adminMode(),
        }),
      );
      const nextState: TaskUiState = {
        running: false,
        startedAt,
        completedAt: new Date().toISOString(),
        result,
        error: null,
        stale: false,
      };
      this.extraTask.set(nextState);
      this.persistExtraTask(nextState);
      await this.refreshDashboardSnapshotAsync();
    } catch (error) {
      const message = this.toErrorMessage(error, 'Не удалось запустить extra-задачу.');
      const nextState: TaskUiState = {
        running: false,
        startedAt,
        completedAt: new Date().toISOString(),
        result: null,
        error: message,
        stale: false,
      };
      this.extraTask.set(nextState);
      this.persistExtraTask(nextState);
      this.errorMessage.set(message);
    }
  }

  async startRunAsync(): Promise<void> {
    if (!this.canStartRun()) {
      return;
    }

    this.errorMessage.set(null);
    this.runEvents.set([]);

    try {
      const response = await firstValueFrom(
        this.http.post<RunAcceptedResponse>(this.apiUrl('/api/runs'), {
          memberCodes: this.selectedMemberCodes(),
          adminOverride: this.adminMode(),
        }),
      );

      this.trackedCorrelationId.set(response.correlationId);
      await this.subscribeToTrackedRunAsync(response.correlationId);
      const run = await this.fetchRunStatusAsync(response.correlationId);
      if (run) {
        this.activeRun.set(run);
        this.ensureRunPolling();
      }
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        const details = (error.error ?? {}) as ProblemDetailsResponse;
        const activeCorrelationId = details.activeCorrelationId;
        if (activeCorrelationId) {
          this.trackedCorrelationId.set(activeCorrelationId);
          await this.subscribeToTrackedRunAsync(activeCorrelationId);
          const run = await this.fetchRunStatusAsync(activeCorrelationId);
          if (run) {
            this.activeRun.set(run);
            this.ensureRunPolling();
          }
        }
        this.errorMessage.set(
          details.detail ?? 'Уже выполняется другой run. Переключаюсь в режим наблюдения.',
        );
        return;
      }

      this.errorMessage.set(this.toErrorMessage(error, 'Не удалось запустить run.'));
    }
  }

  async stopRunAsync(): Promise<void> {
    const correlationId = this.trackedCorrelationId();
    if (!correlationId || !this.isRunBusy()) {
      return;
    }

    this.errorMessage.set(null);
    try {
      await firstValueFrom(
        this.http.post(this.apiUrl(`/api/runs/${encodeURIComponent(correlationId)}/stop`), null),
      );
    } catch (error) {
      this.errorMessage.set(this.toErrorMessage(error, 'Не удалось отправить запрос на остановку.'));
    }
  }

  setAdminMode(enabled: boolean): void {
    this.adminMode.set(enabled);
  }

  private async bootstrapAsync(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [catalog, members, presetState, activeRunPayload, serverTime, dashboard, runsToday] = await Promise.all([
        this.fetchCatalogAsync(),
        this.fetchMembersAsync(),
        this.fetchPresetStateAsync(),
        this.fetchActiveRunAsync(),
        this.fetchServerTimeAsync(),
        this.fetchDashboardSnapshotAsync(),
        this.fetchTodayRunsAsync(),
      ]);

      this.applyServerTime(serverTime);
      this.catalog.set(catalog);
      this.members.set(members);
      this.presetState.set(dashboard.presetState ?? presetState);
      this.hasRunToday.set(Boolean(dashboard.hasRunToday));
      this.hasExtraToday.set(Boolean(dashboard.hasExtraToday));
      this.runLastCompletedAt.set(dashboard.runLastCompletedAt ?? null);
      this.extraLastCompletedAt.set(dashboard.extraLastCompletedAt ?? null);
      this.todayHistory.set(dashboard.todayHistory ?? []);
      const runOnlyToday = (runsToday ?? []).filter((run) => this.isMainRunHistoryEntry(run));
      this.todayRuns.set(runOnlyToday);
      this.recalculateRunTodayFlags(runOnlyToday);
      await this.refreshOutputFilesForHistoryAsync(dashboard.todayHistory ?? []);
      this.reconcileSelection(members);

      const correlationId = activeRunPayload?.correlationId ?? null;
      this.trackedCorrelationId.set(correlationId);
      this.activeRun.set(null);
      this.runEvents.set([]);

      if (correlationId) {
        await this.subscribeToTrackedRunAsync(correlationId);
        const run = isRunStatusPayload(activeRunPayload)
          ? activeRunPayload
          : await this.fetchRunStatusAsync(correlationId);
        if (run) {
          this.activeRun.set(run);
          if (!isTerminalRunStatus(run.status)) {
            this.ensureRunPolling();
          } else {
            this.stopRunPolling();
          }
        }
      } else {
        this.stopRunPolling();
      }

      this.ready.set(true);
    } catch (error) {
      this.errorMessage.set(this.toErrorMessage(error, 'Не удалось загрузить состояние приложения.'));
    } finally {
      this.loading.set(false);
    }
  }

  private async connectRealtimeAsync(): Promise<void> {
    if (!this.browser || this.connection) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl(this.apiUrl('/hubs/status'))
      .withAutomaticReconnect()
      .build();

    connection.on('status', (event: RunnerEvent) => {
      if (!this.shouldProcessCorrelation(event.correlationId)) {
        return;
      }

      this.runEvents.update((current: RunnerEvent[]) => [event, ...current].slice(0, RUN_EVENT_LIMIT));
    });

    connection.on('run_status', (status: RunStatusInfo) => {
      if (!this.shouldProcessCorrelation(status.correlationId)) {
        return;
      }

      this.trackedCorrelationId.set(status.correlationId);
      this.activeRun.set(status);
      if (isTerminalRunStatus(status.status)) {
        // Reflect terminal result immediately in stage card UI,
        // then reconcile with server snapshots.
        this.hasRunToday.set(true);
        if (status.status === RunLifecycleStatus.Completed) {
          this.runLastCompletedAt.set(status.updatedAt);
        }

        this.stopRunPolling();
        void this.refreshTodayRunsAsync();
        void this.refreshDashboardSnapshotAsync();
      } else {
        this.ensureRunPolling();
      }
    });

    connection.on('preset_state', (state: PresetGateState) => {
      this.presetState.set(state);
    });

    connection.onreconnecting(() => {
      this.connectionReady.set(false);
    });

    connection.onclose(() => {
      this.connectionReady.set(false);
    });

    connection.onreconnected(async () => {
      this.connectionReady.set(true);
      await this.subscribeToTrackedRunAsync(this.trackedCorrelationId());
    });

    try {
      await connection.start();
      this.connection = connection;
      this.connectionReady.set(true);
      await this.subscribeToTrackedRunAsync(this.trackedCorrelationId());
    } catch (error) {
      this.connectionReady.set(false);
      if (isDevMode()) {
        console.error(error);
      }
    }
  }

  private async subscribeToTrackedRunAsync(correlationId: string | null): Promise<void> {
    if (!correlationId || !this.connection || this.connection.state !== HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('SubscribeRun', correlationId);
    } catch (error) {
      if (isDevMode()) {
        console.error(error);
      }
    }
  }

  private ensureRunPolling(): void {
    if (!this.browser || this.runPollTimerId) {
      return;
    }

    this.runPollTimerId = window.setInterval(() => {
      void this.refreshTrackedRunAsync();
    }, 2500);
  }

  private stopRunPolling(): void {
    if (!this.browser || this.runPollTimerId === null) {
      return;
    }

    window.clearInterval(this.runPollTimerId);
    this.runPollTimerId = null;
  }

  private async refreshTrackedRunAsync(): Promise<void> {
    const correlationId = this.trackedCorrelationId();
    if (!correlationId) {
      this.stopRunPolling();
      return;
    }

    const state = await this.fetchRunStatusAsync(correlationId);
    if (!state) {
      return;
    }

    this.activeRun.set(state);
    if (isTerminalRunStatus(state.status)) {
      this.stopRunPolling();
      await this.refreshTodayRunsAsync();
    }
  }

  private async refreshPresetStateAsync(): Promise<void> {
    const state = await this.fetchPresetStateAsync();
    this.presetState.set(state);
  }

  private async refreshDashboardSnapshotAsync(): Promise<void> {
    try {
      const snapshot = await this.fetchDashboardSnapshotAsync();
      this.hasRunToday.set(Boolean(snapshot.hasRunToday));
      this.hasExtraToday.set(Boolean(snapshot.hasExtraToday));
      this.runLastCompletedAt.set(snapshot.runLastCompletedAt ?? null);
      this.extraLastCompletedAt.set(snapshot.extraLastCompletedAt ?? null);
      this.todayHistory.set(snapshot.todayHistory ?? []);
      this.presetState.set(snapshot.presetState ?? this.presetState());
      await this.refreshTodayRunsAsync();
      await this.refreshOutputFilesForHistoryAsync(snapshot.todayHistory ?? []);
    } catch (error) {
      if (isDevMode()) {
        console.error(error);
      }
    }
  }

  private async syncServerTimeAsync(): Promise<void> {
    try {
      const serverTime = await this.fetchServerTimeAsync();
      this.applyServerTime(serverTime);
    } catch (error) {
      if (isDevMode()) {
        console.error(error);
      }
    }
  }

  private async fetchCatalogAsync(): Promise<CatalogInfo> {
    return firstValueFrom(this.http.get<CatalogInfo>(this.apiUrl('/api/catalog')));
  }

  private async fetchMembersAsync(): Promise<MemberCatalogItem[]> {
    const members = await firstValueFrom(
      this.http.get<MemberCatalogItem[]>(this.apiUrl('/api/members')),
    );
    return members ?? [];
  }

  private async fetchPresetStateAsync(): Promise<PresetGateState> {
    return firstValueFrom(
      this.http.get<PresetGateState>(this.apiUrl('/api/runs/preset/state')),
    );
  }

  private async fetchServerTimeAsync(): Promise<ServerTimeResponse> {
    return firstValueFrom(
      this.http.get<ServerTimeResponse>(this.apiUrl('/api/system/time')),
    );
  }

  private async fetchDashboardSnapshotAsync(): Promise<WorkflowDashboardSnapshotResponse> {
    return firstValueFrom(
      this.http.get<WorkflowDashboardSnapshotResponse>(this.apiUrl('/api/runs/dashboard')),
    );
  }

  private async fetchTodayRunsAsync(): Promise<RunStatusInfo[]> {
    const runs = await firstValueFrom(this.http.get<RunStatusInfo[]>(this.apiUrl('/api/runs/today')));
    return runs ?? [];
  }

  private async refreshTodayRunsAsync(): Promise<void> {
    try {
      const runs = (await this.fetchTodayRunsAsync()).filter((run) => this.isMainRunHistoryEntry(run));
      this.todayRuns.set(runs);
      this.recalculateRunTodayFlags(runs);
    } catch (error) {
      if (isDevMode()) {
        console.error(error);
      }
    }
  }

  private recalculateRunTodayFlags(runs: RunStatusInfo[]): void {
    const sorted = [...runs].sort(
      (left, right) => new Date(right.createdAt).getTime() - new Date(left.createdAt).getTime(),
    );

    this.hasRunToday.set(sorted.length > 0);

    const completedRun = sorted.find((run) => run.status === RunLifecycleStatus.Completed) ?? null;
    this.runLastCompletedAt.set(completedRun?.updatedAt ?? null);
  }

  private isMainRunHistoryEntry(run: RunStatusInfo): boolean {
    const taskCode = (run.taskCode ?? '').trim().toLowerCase();
    const correlationId = (run.correlationId ?? '').trim().toLowerCase();
    return taskCode === 'run' && correlationId.startsWith('req-');
  }

  private async refreshOutputFilesForHistoryAsync(history: TaskRecord[]): Promise<void> {
    const paths = history
      .filter(
        (record) =>
          (record.taskCode === 'extra' || record.taskCode === 'preset') &&
          !!record.outputPath,
      )
      .map((record) => record.outputPath as string);
    if (paths.length === 0) {
      return;
    }

    const cache = { ...this.outputFilesByPath() };
    for (const path of new Set(paths)) {
      if (cache[path]) {
        continue;
      }

      try {
        const files = await firstValueFrom(
          this.http.get<OutputFileInfo[]>(
            this.apiUrl(`/api/system/output-files?path=${encodeURIComponent(path)}`),
          ),
        );
        cache[path] = files ?? [];
      } catch (error) {
        cache[path] = [];
        if (isDevMode()) {
          console.error(error);
        }
      }
    }

    this.outputFilesByPath.set(cache);
  }

  private async fetchActiveRunAsync(): Promise<RunStatusInfo | ActiveRunPayload | null> {
    try {
      const payload = await firstValueFrom(
        this.http.get<RunStatusInfo | ActiveRunPayload>(this.apiUrl('/api/runs/active')),
      );
      return payload ?? null;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        return null;
      }

      throw error;
    }
  }

  private async fetchRunStatusAsync(correlationId: string): Promise<RunStatusInfo | null> {
    try {
      return await firstValueFrom(
        this.http.get<RunStatusInfo>(this.apiUrl(`/api/runs/${encodeURIComponent(correlationId)}`)),
      );
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        return null;
      }

      throw error;
    }
  }

  private reconcileSelection(members: MemberCatalogItem[]): void {
    const availableCodes = members.map((member) => member.code);
    const storedSelection = this.readSelectedMemberCodes();
    const filtered =
      storedSelection === null
        ? availableCodes
        : availableCodes.filter((code) => storedSelection.includes(code));
    const nextCodes = filtered.length > 0 ? filtered : availableCodes;

    this.selectedMemberCodes.set(sortCodes(nextCodes));
    this.persistSelection();
  }

  private shouldProcessCorrelation(correlationId: string): boolean {
    const tracked = this.trackedCorrelationId();
    return !tracked || tracked === correlationId;
  }

  private apiUrl(path: string): string {
    return `${this.apiBaseUrl}${path}`;
  }

  private applyServerTime(serverTime: ServerTimeResponse): void {
    this.serverTimeOffsetMs =
      new Date(serverTime.serverLocalTime).getTime() - Date.now();
    this.serverTimeZoneId.set(serverTime.timeZoneId);
    this.updateCurrentTime();
  }

  private updateCurrentTime(): void {
    this.currentTime.set(new Date(Date.now() + this.serverTimeOffsetMs));
  }

  private persistSelection(): void {
    if (!this.browser) {
      return;
    }

    window.localStorage.setItem(
      MEMBER_SELECTION_STORAGE_KEY,
      JSON.stringify(this.selectedMemberCodes()),
    );
  }

  private readSelectedMemberCodes(): string[] | null {
    if (!this.browser) {
      return null;
    }

    try {
      const raw = window.localStorage.getItem(MEMBER_SELECTION_STORAGE_KEY);
      if (!raw) {
        return null;
      }

      const payload = JSON.parse(raw) as unknown;
      return Array.isArray(payload)
        ? payload.filter((item): item is string => typeof item === 'string')
        : null;
    } catch {
      return null;
    }
  }

  private persistExtraTask(state: TaskUiState): void {
    if (!this.browser) {
      return;
    }

    window.localStorage.setItem(EXTRA_TASK_STORAGE_KEY, JSON.stringify(state));
  }

  private persistPresetTask(state: TaskUiState): void {
    if (!this.browser) {
      return;
    }

    window.localStorage.setItem(PRESET_TASK_STORAGE_KEY, JSON.stringify(state));
  }

  private restoreExtraTaskState(): void {
    try {
      const raw = window.localStorage.getItem(EXTRA_TASK_STORAGE_KEY);
      if (!raw) {
        return;
      }

      const payload = JSON.parse(raw) as Partial<TaskUiState> | null;
      if (!payload) {
        return;
      }

      this.extraTask.set({
        running: false,
        startedAt: typeof payload.startedAt === 'string' ? payload.startedAt : null,
        completedAt: typeof payload.completedAt === 'string' ? payload.completedAt : null,
        result: payload.result ?? null,
        error: payload.error ?? null,
        stale: Boolean(payload.running),
      });
    } catch {
      this.extraTask.set(createIdleTaskState());
    }
  }

  private restorePresetTaskState(): void {
    try {
      const raw = window.localStorage.getItem(PRESET_TASK_STORAGE_KEY);
      if (!raw) {
        return;
      }

      const payload = JSON.parse(raw) as Partial<TaskUiState> | null;
      if (!payload) {
        return;
      }

      this.presetTask.set({
        running: false,
        startedAt: typeof payload.startedAt === 'string' ? payload.startedAt : null,
        completedAt: typeof payload.completedAt === 'string' ? payload.completedAt : null,
        result: payload.result ?? null,
        error: payload.error ?? null,
        stale: Boolean(payload.running),
      });
    } catch {
      this.presetTask.set(createIdleTaskState());
    }
  }

  private toErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      if (typeof error.error === 'string' && error.error.trim()) {
        return error.error;
      }

      const details = error.error as ProblemDetailsResponse | null;
      if (details?.detail) {
        return details.detail;
      }
    }

    if (error instanceof Error && error.message.trim()) {
      return error.message;
    }

    return fallback;
  }
}

function buildMemberGroups(
  catalog: CatalogInfo | null,
  members: MemberCatalogItem[],
  activeRun: RunStatusInfo | null,
  events: RunnerEvent[],
  selectedMemberCodes: string[],
): MemberGroupViewModel[] {
  if (!catalog) {
    return [];
  }

  const selected = new Set(selectedMemberCodes);
  const membersByCode = new Map(members.map((member) => [member.code, member]));
  const memberLogs = buildMemberLogs(activeRun, events);
  const memberArtifacts = buildMemberArtifacts(activeRun?.outputArtifacts ?? []);
  const memberStatuses = activeRun?.memberStatuses ?? {};

  return catalog.groups
    .map((group) => {
      const groupTargets = catalog.targets.filter((target) => target.groupId === group.id);
      const memberCodes = Array.from(new Set(groupTargets.map((target) => target.memberCode)));

      const groupMembers = memberCodes
        .map((memberCode) => {
          const catalogMember = catalog.members.find((member) => member.code === memberCode);
          const memberRecord = membersByCode.get(memberCode);
          if (!catalogMember || !memberRecord) {
            return null;
          }

          const status = resolveMemberStatus(
            memberRecord,
            memberStatuses[catalogMember.name],
          );

          return {
            code: memberCode,
            name: catalogMember.name,
            targetCodes: groupTargets
              .filter((target) => target.memberCode === memberCode)
              .map((target) => target.targetCode)
              .sort((left, right) => left.localeCompare(right)),
            selected: selected.has(memberCode),
            status: status.status,
            lastStep: status.lastStep,
            message: status.message,
            updatedAt: status.updatedAt,
            logs: memberLogs.get(catalogMember.name) ?? [],
            outputArtifacts: memberArtifacts.get(catalogMember.name) ?? [],
          } satisfies MemberViewModel;
        })
        .filter((member): member is MemberViewModel => !!member)
        .sort((left, right) => left.name.localeCompare(right.name));

      return {
        id: group.id,
        name: group.name,
        folder: group.folder,
        members: groupMembers,
      } satisfies MemberGroupViewModel;
    })
    .filter((group) => group.members.length > 0)
    .sort((left, right) => left.name.localeCompare(right.name));
}

function resolveMemberStatus(
  member: MemberCatalogItem,
  liveStatus: MemberRunStatusInfo | undefined,
): {
  status: MemberRunLifecycleStatus;
  lastStep: RunnerStep | null;
  message: string | null;
  updatedAt: string | null;
} {
  const status = liveStatus ?? member.activeRunStatus;
  if (status) {
    return {
      status: status.status,
      lastStep: status.lastStep,
      message: status.message,
      updatedAt: status.updatedAt,
    };
  }

  return {
    status: MemberRunLifecycleStatus.Pending,
    lastStep: null,
    message: 'Готов к запуску.',
    updatedAt: null,
  };
}

function buildMemberLogs(
  activeRun: RunStatusInfo | null,
  events: RunnerEvent[],
): Map<string, MemberLogLine[]> {
  const result = new Map<string, MemberLogLine[]>();

  if (activeRun?.memberStatuses) {
    for (const member of Object.values(activeRun.memberStatuses)) {
      result.set(member.memberName, [
        {
          time: member.updatedAt,
          step: member.lastStep ?? RunnerStep.RequestAccepted,
          message: member.message ?? 'Статус обновлен.',
        },
      ]);
    }
  }

  for (const event of events) {
    if (!event.memberName) {
      continue;
    }

    const current = result.get(event.memberName) ?? [];
    const line = {
      time: event.occurredAt,
      step: event.step,
      message: event.message,
    } satisfies MemberLogLine;

    if (!current.some((item) => item.time === line.time && item.step === line.step && item.message === line.message)) {
      current.unshift(line);
    }

    result.set(event.memberName, current.slice(0, 6));
  }

  return result;
}

function buildMemberArtifacts(
  artifacts: RunOutputArtifactInfo[],
): Map<string, RunOutputArtifactInfo[]> {
  const result = new Map<string, RunOutputArtifactInfo[]>();

  for (const artifact of artifacts) {
    if (!artifact.memberName) {
      continue;
    }

    const current = result.get(artifact.memberName) ?? [];
    current.push(artifact);
    current.sort(
      (left, right) =>
        new Date(right.occurredAt).getTime() - new Date(left.occurredAt).getTime(),
    );
    result.set(artifact.memberName, current);
  }

  return result;
}

function resolveApiBaseUrl(): string {
  if (typeof window === 'undefined') {
    return '';
  }

  const configured = (window as Window & { __UNLOAD_API_BASE_URL__?: string }).__UNLOAD_API_BASE_URL__;
  if (configured) {
    return configured.replace(/\/$/, '');
  }

  return '';
}

function createIdleTaskState(): TaskUiState {
  return {
    running: false,
    startedAt: null,
    completedAt: null,
    result: null,
    error: null,
    stale: false,
  };
}

function isTerminalRunStatus(status: RunLifecycleStatus): boolean {
  return (
    status === RunLifecycleStatus.Completed ||
    status === RunLifecycleStatus.Failed ||
    status === RunLifecycleStatus.Cancelled
  );
}

function isRunStatusPayload(
  payload: RunStatusInfo | ActiveRunPayload | null,
): payload is RunStatusInfo {
  return !!payload && typeof payload === 'object' && 'createdAt' in payload;
}

function sortCodes(codes: Iterable<string>): string[] {
  return Array.from(new Set(codes)).sort((left, right) => left.localeCompare(right));
}
