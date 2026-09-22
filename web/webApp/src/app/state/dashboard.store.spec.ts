import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { RunLifecycleStatus, RunStatusInfo } from '../app.models';
import { ApiClientService } from './api-client.service';
import { DashboardStore } from './dashboard.store';
import { OutputFilesStore } from './output-files.store';
import { PresetStore } from './preset.store';

function run(): RunStatusInfo {
  return {
    correlationId: 'req-owned',
    taskCode: 'run',
    status: RunLifecycleStatus.Completed,
    createdAt: '2026-09-22T10:00:00.000Z',
    updatedAt: '2026-09-22T10:00:03.000Z',
  } as RunStatusInfo;
}

describe('DashboardStore run-day projection', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        DashboardStore,
        { provide: ApiClientService, useValue: {} },
        { provide: OutputFilesStore, useValue: { refreshForHistory: vi.fn() } },
        {
          provide: PresetStore,
          useValue: { presetState: signal(null), setPresetState: vi.fn() },
        },
      ],
    });
  });

  afterEach(() => TestBed.resetTestingModule());

  it('keeps a terminal run visible when stale dashboard and empty history arrive', () => {
    const store = TestBed.inject(DashboardStore);
    store.markRunCompleted(run());

    store.applySnapshot({ hasRunToday: false, hasExtraToday: false } as never);
    store.applyTodayRuns([]);

    expect(store.hasRunToday()).toBe(true);
    expect(store.runLastCompletedAt()).toBe('2026-09-22T10:00:03.000Z');
  });

  it('clears the optimistic fact only when the server day changes', () => {
    const store = TestBed.inject(DashboardStore);
    store.markRunCompleted(run());
    store.resetForNewServerDay();

    store.applySnapshot({ hasRunToday: false, hasExtraToday: false } as never);
    expect(store.hasRunToday()).toBe(false);
    expect(store.todayRuns()).toEqual([]);
    expect(store.allTodayRuns()).toEqual([]);
    expect(store.todayHistory()).toEqual([]);
  });

  it('ignores a stale today-runs response that finishes after rollover', async () => {
    let resolveRuns!: (runs: RunStatusInfo[]) => void;
    const api = {
      fetchTodayRuns: vi.fn(
        () => new Promise<RunStatusInfo[]>((resolve) => (resolveRuns = resolve)),
      ),
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        DashboardStore,
        { provide: ApiClientService, useValue: api },
        { provide: OutputFilesStore, useValue: { refreshForHistory: vi.fn() } },
        {
          provide: PresetStore,
          useValue: { presetState: signal(null), setPresetState: vi.fn() },
        },
      ],
    });
    const store = TestBed.inject(DashboardStore);
    const refresh = store.refreshTodayRunsAsync();
    store.resetForNewServerDay();
    resolveRuns([run()]);
    await refresh;

    expect(store.todayRuns()).toEqual([]);
    expect(store.allTodayRuns()).toEqual([]);
    expect(store.hasRunToday()).toBe(false);
  });

  it('ignores a stale dashboard response that finishes after rollover', async () => {
    let resolveSnapshot!: (snapshot: unknown) => void;
    const api = {
      fetchDashboardSnapshot: vi.fn(
        () => new Promise<unknown>((resolve) => (resolveSnapshot = resolve)),
      ),
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        DashboardStore,
        { provide: ApiClientService, useValue: api },
        { provide: OutputFilesStore, useValue: { refreshForHistory: vi.fn() } },
        {
          provide: PresetStore,
          useValue: { presetState: signal(null), setPresetState: vi.fn() },
        },
      ],
    });
    const store = TestBed.inject(DashboardStore);
    const refresh = store.refreshDashboardAsync();
    store.resetForNewServerDay();
    resolveSnapshot({
      hasRunToday: true,
      todayHistory: [{ taskCode: 'run', startedAt: 'old-day' }],
    });
    await refresh;

    expect(store.hasRunToday()).toBe(false);
    expect(store.todayHistory()).toEqual([]);
  });

  it('keeps the newest extra completion timestamp against stale history', () => {
    const store = TestBed.inject(DashboardStore);
    store.applySnapshot({
      hasExtraToday: true,
      extraLastCompletedAt: '2026-09-22T10:00:05.000Z',
    } as never);
    store.applyTodayRuns([
      {
        correlationId: 'extra-old',
        taskCode: 'extra',
        status: RunLifecycleStatus.Completed,
        createdAt: '2026-09-22T10:00:00.000Z',
        updatedAt: '2026-09-22T10:00:03.000Z',
      } as RunStatusInfo,
    ]);

    expect(store.extraLastCompletedAt()).toBe('2026-09-22T10:00:05.000Z');
  });
});
