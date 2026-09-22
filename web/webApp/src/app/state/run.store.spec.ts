import { PLATFORM_ID, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { EMPTY, Subject } from 'rxjs';
import { vi } from 'vitest';
import { RunLifecycleStatus, RunStatusInfo } from '../app.models';
import { AdminStore } from './admin.store';
import { ApiClientService } from './api-client.service';
import { CatalogStore } from './catalog.store';
import { DashboardStore } from './dashboard.store';
import { RunStore } from './run.store';
import { RealtimeHubService } from './realtime-hub.service';
import { SelectionStore } from './selection.store';
import { WorkflowErrorStore } from './error.store';

function run(
  status: RunLifecycleStatus = RunLifecycleStatus.Running,
  overrides: Partial<RunStatusInfo> = {},
): RunStatusInfo {
  return {
    correlationId: 'req-owned',
    taskCode: 'run',
    status,
    createdAt: '2026-09-22T10:00:00.000Z',
    updatedAt: '2026-09-22T10:00:01.000Z',
    ...overrides,
  } as RunStatusInfo;
}

describe('RunStore launch lifecycle', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('allows only one POST while the first launch is slow', async () => {
    let resolveStart!: (response: { correlationId: string }) => void;
    const api = {
      startRun: vi.fn(
        () => new Promise<{ correlationId: string }>((resolve) => (resolveStart = resolve)),
      ),
      fetchRunStatus: vi.fn().mockResolvedValue(run()),
      fetchActiveRun: vi.fn().mockResolvedValue(run()),
    };
    const hub = {
      statusEvents$: EMPTY,
      runStatusEvents$: EMPTY,
      reconnected$: EMPTY,
      connectionReady: signal(true),
      subscribeRun: vi.fn().mockResolvedValue(undefined),
    };

    TestBed.configureTestingModule({
      providers: [
        RunStore,
        { provide: ApiClientService, useValue: api },
        { provide: CatalogStore, useValue: { catalog: signal({ targets: [] }) } },
        {
          provide: SelectionStore,
          useValue: { selectedTargetCodes: signal([]), resolveSelectedMemberCodes: () => [] },
        },
        {
          provide: DashboardStore,
          useValue: {
            markRunCompleted: vi.fn(),
            refreshTodayRunsAsync: vi.fn(),
            refreshDashboardAsync: vi.fn(),
          },
        },
        { provide: AdminStore, useValue: { adminMode: signal(false) } },
        { provide: WorkflowErrorStore, useValue: { clear: vi.fn(), setError: vi.fn() } },
        { provide: RealtimeHubService, useValue: hub },
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });

    const store = TestBed.inject(RunStore);
    const first = store.startRunAsync(true);
    await Promise.resolve();
    const second = store.startRunAsync(true);
    await Promise.resolve();

    expect(api.startRun).toHaveBeenCalledTimes(1);
    expect(store.isRunBusy()).toBe(true);
    expect(api.fetchActiveRun).not.toHaveBeenCalled();

    resolveStart({ correlationId: 'req-owned' });
    await first;
    await second;
    expect(store.trackedCorrelationId()).toBe('req-owned');
  });

  it('keeps a terminal run busy until GET active confirms channel release', async () => {
    const api = {
      fetchRunStatus: vi.fn().mockResolvedValue(null),
      fetchActiveRun: vi.fn().mockResolvedValue(run(RunLifecycleStatus.Completed)),
    };
    const hub = {
      statusEvents$: EMPTY,
      runStatusEvents$: EMPTY,
      reconnected$: EMPTY,
      connectionReady: signal(true),
      subscribeRun: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        RunStore,
        { provide: ApiClientService, useValue: api },
        { provide: CatalogStore, useValue: {} },
        { provide: SelectionStore, useValue: {} },
        {
          provide: DashboardStore,
          useValue: { markRunCompleted: vi.fn(), refreshTodayRunsAsync: vi.fn() },
        },
        { provide: AdminStore, useValue: {} },
        { provide: WorkflowErrorStore, useValue: { clear: vi.fn(), setError: vi.fn() } },
        { provide: RealtimeHubService, useValue: hub },
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });
    const store = TestBed.inject(RunStore);
    store.applyInitialActiveRun(run(RunLifecycleStatus.Completed));
    await store.syncActiveRunAsync('req-owned', null);
    expect(store.isRunBusy()).toBe(true);

    api.fetchActiveRun.mockResolvedValue(null);
    await store.syncActiveRunAsync('req-owned', null);
    expect(store.isRunBusy()).toBe(false);
  });

  it('keeps an accepted launch busy while status reconciliation is unresolved', async () => {
    let resolveActive!: (status: RunStatusInfo | null) => void;
    const api = {
      startRun: vi.fn().mockResolvedValue({ correlationId: 'req-owned' }),
      fetchRunStatus: vi.fn().mockResolvedValue(null),
      fetchActiveRun: vi.fn(
        () => new Promise<RunStatusInfo | null>((resolve) => (resolveActive = resolve)),
      ),
    };
    const hub = {
      statusEvents$: EMPTY,
      runStatusEvents$: EMPTY,
      reconnected$: EMPTY,
      connectionReady: signal(true),
      subscribeRun: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        RunStore,
        { provide: ApiClientService, useValue: api },
        { provide: CatalogStore, useValue: { catalog: signal({ targets: [] }) } },
        { provide: SelectionStore, useValue: { selectedTargetCodes: signal([]), resolveSelectedMemberCodes: () => [] } },
        {
          provide: DashboardStore,
          useValue: {
            markRunCompleted: vi.fn(),
            refreshTodayRunsAsync: vi.fn(),
            refreshDashboardAsync: vi.fn(),
          },
        },
        { provide: AdminStore, useValue: { adminMode: signal(false) } },
        { provide: WorkflowErrorStore, useValue: { clear: vi.fn(), setError: vi.fn() } },
        { provide: RealtimeHubService, useValue: hub },
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });
    const store = TestBed.inject(RunStore);
    const first = store.startRunAsync(true);
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    expect(store.isRunBusy()).toBe(true);
    await store.startRunAsync(true);
    expect(api.startRun).toHaveBeenCalledTimes(1);

    resolveActive(null);
    await first;
    expect(store.isRunBusy()).toBe(false);
  });

  it('keeps an accepted launch busy after a status network error while active is occupied', async () => {
    const active = run();
    const api = {
      startRun: vi.fn().mockResolvedValue({ correlationId: 'req-owned' }),
      fetchRunStatus: vi.fn().mockRejectedValue(new Error('status unavailable')),
      fetchActiveRun: vi.fn().mockResolvedValue(active),
    };
    const hub = {
      statusEvents$: EMPTY,
      runStatusEvents$: EMPTY,
      reconnected$: EMPTY,
      connectionReady: signal(true),
      subscribeRun: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        RunStore,
        { provide: ApiClientService, useValue: api },
        { provide: CatalogStore, useValue: { catalog: signal({ targets: [] }) } },
        { provide: SelectionStore, useValue: { selectedTargetCodes: signal([]), resolveSelectedMemberCodes: () => [] } },
        {
          provide: DashboardStore,
          useValue: {
            markRunCompleted: vi.fn(),
            refreshTodayRunsAsync: vi.fn(),
            refreshDashboardAsync: vi.fn(),
          },
        },
        { provide: AdminStore, useValue: { adminMode: signal(false) } },
        { provide: WorkflowErrorStore, useValue: { clear: vi.fn(), setError: vi.fn() } },
        { provide: RealtimeHubService, useValue: hub },
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });
    const store = TestBed.inject(RunStore);
    await store.startRunAsync(true);

    expect(store.isRunBusy()).toBe(true);
    expect(api.startRun).toHaveBeenCalledTimes(1);
    api.fetchActiveRun.mockResolvedValue(null);
    await store.syncActiveRunAsync('req-owned', null);
    expect(store.isRunBusy()).toBe(false);
  });

  it('reconciles terminal status after a missed reconnect event', async () => {
    const reconnected = new Subject<void>();
    const api = {
      fetchRunStatus: vi.fn().mockResolvedValue(run(RunLifecycleStatus.Completed)),
      fetchActiveRun: vi.fn().mockResolvedValue(run(RunLifecycleStatus.Completed)),
    };
    const hub = {
      statusEvents$: EMPTY,
      runStatusEvents$: EMPTY,
      reconnected$: reconnected.asObservable(),
      connectionReady: signal(true),
      subscribeRun: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        RunStore,
        { provide: ApiClientService, useValue: api },
        { provide: CatalogStore, useValue: {} },
        { provide: SelectionStore, useValue: {} },
        { provide: DashboardStore, useValue: { markRunCompleted: vi.fn(), refreshTodayRunsAsync: vi.fn() } },
        { provide: AdminStore, useValue: {} },
        { provide: WorkflowErrorStore, useValue: { clear: vi.fn(), setError: vi.fn() } },
        { provide: RealtimeHubService, useValue: hub },
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });
    const store = TestBed.inject(RunStore);
    store.applyInitialActiveRun(run(RunLifecycleStatus.Running));
    reconnected.next();
    await Promise.resolve();
    await Promise.resolve();

    expect(store.activeRun()?.status).toBe(RunLifecycleStatus.Completed);
  });

  it('keeps a rejected POST busy while active reconciliation finds a run', async () => {
    let resolveActive!: (status: RunStatusInfo | null) => void;
    const api = {
      startRun: vi.fn().mockRejectedValue(new Error('network down')),
      fetchRunStatus: vi.fn().mockResolvedValue(null),
      fetchActiveRun: vi.fn(
        () => new Promise<RunStatusInfo | null>((resolve) => (resolveActive = resolve)),
      ),
    };
    const hub = {
      statusEvents$: EMPTY,
      runStatusEvents$: EMPTY,
      reconnected$: EMPTY,
      connectionReady: signal(true),
      subscribeRun: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        RunStore,
        { provide: ApiClientService, useValue: api },
        { provide: CatalogStore, useValue: { catalog: signal({ targets: [] }) } },
        { provide: SelectionStore, useValue: { selectedTargetCodes: signal([]), resolveSelectedMemberCodes: () => [] } },
        {
          provide: DashboardStore,
          useValue: {
            markRunCompleted: vi.fn(),
            refreshTodayRunsAsync: vi.fn(),
            refreshDashboardAsync: vi.fn(),
          },
        },
        { provide: AdminStore, useValue: { adminMode: signal(false) } },
        { provide: WorkflowErrorStore, useValue: { clear: vi.fn(), setError: vi.fn() } },
        { provide: RealtimeHubService, useValue: hub },
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });
    const store = TestBed.inject(RunStore);
    const first = store.startRunAsync(true);
    await Promise.resolve();
    await Promise.resolve();
    expect(store.isRunBusy()).toBe(true);
    await store.startRunAsync(true);
    expect(api.startRun).toHaveBeenCalledTimes(1);

    resolveActive(run());
    await first;
    expect(store.activeRun()?.correlationId).toBe('req-owned');
    expect(store.isRunBusy()).toBe(true);

    api.fetchActiveRun.mockResolvedValue(null);
    await store.syncActiveRunAsync('req-owned', null);
    expect(store.isRunBusy()).toBe(false);
  });

  it('does not let an old terminal reconciliation clear a newer pending launch', async () => {
    let resolveStart!: (response: { correlationId: string }) => void;
    const runStatusEvents = new Subject<RunStatusInfo>();
    const api = {
      startRun: vi
        .fn()
        .mockResolvedValueOnce({ correlationId: 'req-old' })
        .mockImplementationOnce(
          () => new Promise<{ correlationId: string }>((resolve) => (resolveStart = resolve)),
        ),
      fetchRunStatus: vi
        .fn()
        .mockResolvedValueOnce(
          run(RunLifecycleStatus.Completed, { correlationId: 'req-old' }),
        )
        .mockResolvedValue(run(RunLifecycleStatus.Running, { correlationId: 'req-new' })),
      fetchActiveRun: vi.fn().mockResolvedValue(null),
    };
    const hub = {
      statusEvents$: EMPTY,
      runStatusEvents$: runStatusEvents.asObservable(),
      reconnected$: EMPTY,
      connectionReady: signal(true),
      subscribeRun: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        RunStore,
        { provide: ApiClientService, useValue: api },
        { provide: CatalogStore, useValue: { catalog: signal({ targets: [] }) } },
        { provide: SelectionStore, useValue: { selectedTargetCodes: signal([]), resolveSelectedMemberCodes: () => [] } },
        {
          provide: DashboardStore,
          useValue: {
            markRunCompleted: vi.fn(),
            refreshTodayRunsAsync: vi.fn(),
            refreshDashboardAsync: vi.fn(),
          },
        },
        { provide: AdminStore, useValue: { adminMode: signal(false) } },
        { provide: WorkflowErrorStore, useValue: { clear: vi.fn(), setError: vi.fn() } },
        { provide: RealtimeHubService, useValue: hub },
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });
    const store = TestBed.inject(RunStore);
    await store.startRunAsync(true);
    expect(store.isRunBusy()).toBe(false);

    const second = store.startRunAsync(true);
    await Promise.resolve();
    runStatusEvents.next(
      run(RunLifecycleStatus.Completed, { correlationId: 'req-old' }),
    );
    await Promise.resolve();
    expect(store.activeRun()?.correlationId).toBe('req-old');
    expect(store.activeRun()?.status).toBe(RunLifecycleStatus.Completed);
    expect(store.isRunBusy()).toBe(true);
    await store.startRunAsync(true);
    expect(api.startRun).toHaveBeenCalledTimes(2);

    resolveStart({ correlationId: 'req-new' });
    await second;
    expect(store.trackedCorrelationId()).toBe('req-new');
  });

  it('keeps a 409 without correlation busy until active is confirmed absent', async () => {
    let resolveActive!: (status: RunStatusInfo | null) => void;
    const api = {
      startRun: vi.fn().mockRejectedValue(
        new HttpErrorResponse({ status: 409, error: { detail: 'conflict' } }),
      ),
      fetchRunStatus: vi.fn().mockResolvedValue(null),
      fetchActiveRun: vi.fn(
        () => new Promise<RunStatusInfo | null>((resolve) => (resolveActive = resolve)),
      ),
    };
    const hub = {
      statusEvents$: EMPTY,
      runStatusEvents$: EMPTY,
      reconnected$: EMPTY,
      connectionReady: signal(true),
      subscribeRun: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        RunStore,
        { provide: ApiClientService, useValue: api },
        { provide: CatalogStore, useValue: { catalog: signal({ targets: [] }) } },
        { provide: SelectionStore, useValue: { selectedTargetCodes: signal([]), resolveSelectedMemberCodes: () => [] } },
        { provide: DashboardStore, useValue: { markRunCompleted: vi.fn(), refreshTodayRunsAsync: vi.fn() } },
        { provide: AdminStore, useValue: { adminMode: signal(false) } },
        { provide: WorkflowErrorStore, useValue: { clear: vi.fn(), setError: vi.fn() } },
        { provide: RealtimeHubService, useValue: hub },
        { provide: PLATFORM_ID, useValue: 'browser' },
      ],
    });
    const store = TestBed.inject(RunStore);
    const first = store.startRunAsync(true);
    await Promise.resolve();
    await Promise.resolve();
    expect(store.isRunBusy()).toBe(true);
    await store.startRunAsync(true);
    expect(api.startRun).toHaveBeenCalledTimes(1);

    resolveActive(null);
    await first;
    expect(store.isRunBusy()).toBe(false);
  });
});
