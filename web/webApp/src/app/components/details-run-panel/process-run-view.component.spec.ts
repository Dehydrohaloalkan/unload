import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import {
  FileRunStage,
  MemberRunLifecycleStatus,
  RunLifecycleStatus,
  RunStatusInfo,
  ScriptRunStage,
  SenderBatchStatus,
} from '../../app.models';
import { RU } from '../../i18n/ru';
import { WorkflowStore } from '../../app.store';
import { ProcessRunViewComponent } from './process-run-view.component';

describe('ProcessRunViewComponent', () => {
  const activeRun = signal<RunStatusInfo | null>(null);
  const isRunBusy = signal(false);

  beforeEach(async () => {
    activeRun.set(null);
    isRunBusy.set(false);
    await TestBed.configureTestingModule({
      imports: [ProcessRunViewComponent],
      providers: [{ provide: WorkflowStore, useValue: { activeRun, isRunBusy } }],
    }).compileComponents();
  });

  it('renders a helpful empty state before a run snapshot arrives', () => {
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(RU['process.emptyNoRun']);
  });

  it('renders the process empty state when analysis has not created cards yet', () => {
    activeRun.set({
      correlationId: 'run-1',
      taskCode: 'run',
      status: RunLifecycleStatus.Running,
      targetCodes: [],
      createdAt: '2026-09-21T10:00:00Z',
      updatedAt: '2026-09-21T10:00:00Z',
      memberStatuses: {},
      scriptStatuses: {},
      fileStatuses: {},
      senderBatches: {},
    });
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(RU['process.emptyNoCards']);
    expect(fixture.nativeElement.querySelector('[aria-live="polite"]')).not.toBeNull();
  });

  it('renders one vertical pipeline with all zones and exactly four workers', () => {
    activeRun.set({
      correlationId: 'run-2',
      taskCode: 'run',
      status: RunLifecycleStatus.Running,
      targetCodes: [],
      createdAt: '2026-09-21T10:00:00Z',
      updatedAt: '2026-09-21T10:02:00Z',
      memberStatuses: {
        bank: {
          memberName: 'Bank A',
          status: MemberRunLifecycleStatus.Running,
          lastStep: null,
          message: null,
          updatedAt: '2026-09-21T10:02:00Z',
        },
      },
      scriptStatuses: {
        script: {
          id: 'script-1',
          memberName: 'Bank A',
          scriptCode: 'SCRIPT_1',
          stage: ScriptRunStage.Running,
          discoveredAt: '2026-09-21T10:00:00Z',
          stageEnteredAt: '2026-09-21T10:01:00Z',
          startedAt: '2026-09-21T10:01:00Z',
          updatedAt: '2026-09-21T10:02:00Z',
          completedAt: null,
          workerId: 2,
          records: 42,
        },
      },
      fileStatuses: {
        file: {
          id: 'file-1',
          parentScriptId: 'script-1',
          memberName: 'Bank A',
          scriptCode: 'SCRIPT_1',
          chunkNumber: 1,
          stage: FileRunStage.Failed,
          createdAt: '2026-09-21T10:01:10Z',
          queuedAt: '2026-09-21T10:01:10Z',
          stageEnteredAt: '2026-09-21T10:01:10Z',
          updatedAt: '2026-09-21T10:02:00Z',
          completedAt: '2026-09-21T10:02:00Z',
          workerId: 2,
          rows: 42,
          estimatedBytes: 100,
          fileName: 'data-file.txt',
          filePath: '/tmp/data-file.txt',
        },
      },
      senderBatches: {
        batch: {
          batchId: 'batch-1',
          memberName: 'Bank A',
          status: SenderBatchStatus.Completed,
          updatedAt: '2026-09-21T10:02:00Z',
          queuedAt: '2026-09-21T10:01:30Z',
          startedAt: '2026-09-21T10:01:40Z',
          fileCount: 1,
          sentFiles: [{ filePath: '/tmp/data-file.txt', sentAt: '2026-09-21T10:02:00Z' }],
        },
      },
    });
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('[data-testid="process-pipeline"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="process-zone-members"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="process-node-resolver"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="process-zone-scripts"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="process-zone-files"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="process-node-sender"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="process-zone-delivered"]')).not.toBeNull();
    expect(host.querySelectorAll('[data-testid^="process-worker-"]')).toHaveLength(4);
    expect(host.querySelector('[data-testid="process-worker-2"]')?.textContent).toContain(
      'SCRIPT_1',
    );
    expect(host.querySelectorAll('[data-testid^="process-file-group-"]')).toHaveLength(1);
  });

  it('does not rebuild the static process projection when only the duration clock ticks', () => {
    activeRun.set({
      correlationId: 'run-clock',
      taskCode: 'run',
      status: RunLifecycleStatus.Running,
      targetCodes: [],
      createdAt: '2026-09-21T10:00:00Z',
      updatedAt: '2026-09-21T10:00:00Z',
      scriptStatuses: {
        script: {
          id: 'script',
          memberName: 'Bank A',
          scriptCode: 'SCRIPT',
          stage: ScriptRunStage.Running,
          discoveredAt: '2026-09-21T10:00:00Z',
          stageEnteredAt: '2026-09-21T10:00:00Z',
          startedAt: '2026-09-21T10:00:00Z',
          updatedAt: '2026-09-21T10:00:00Z',
          workerId: 1,
        },
      },
    });
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const firstPipeline = fixture.componentInstance.pipeline();

    fixture.componentInstance.now.set(new Date('2026-09-21T10:01:00Z'));
    fixture.detectChanges();

    expect(fixture.componentInstance.pipeline()).toBe(firstPipeline);
    expect(fixture.nativeElement.textContent).toContain('00:01:00');
  });

  it('keeps a resolver failure on the resolver node and opens safe details with keyboard close', () => {
    activeRun.set({
      correlationId: 'run-member-failure',
      taskCode: 'run',
      status: RunLifecycleStatus.Failed,
      targetCodes: [],
      createdAt: '2026-09-21T10:00:00Z',
      updatedAt: '2026-09-21T10:01:00Z',
      memberStatuses: {
        bank: {
          memberName: 'Bank failed in resolver',
          status: MemberRunLifecycleStatus.Failed,
          lastStep: null,
          message: '<img src=x onerror=alert(1)> safe resolver failure',
          updatedAt: '2026-09-21T10:01:00Z',
          failure: failure(
            'member',
            'resolver',
            'resolver-code',
            '<img src=x onerror=alert(1)> safe resolver failure',
          ),
        },
      },
    });
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    const resolver = host.querySelector<HTMLElement>('[data-testid="process-node-resolver"]');
    const failureButton = resolver?.querySelector<HTMLButtonElement>('button');
    expect(failureButton).not.toBeNull();
    failureButton?.click();
    fixture.detectChanges();

    const dialog = host.querySelector<HTMLElement>('[data-testid="process-error-details"]');
    expect(dialog?.getAttribute('data-testid')).toBe('process-error-details');
    expect(dialog?.querySelector('[role="dialog"]')).not.toBeNull();
    expect(dialog?.textContent).toContain('resolver-code');
    expect(dialog?.textContent).toContain('<img src=x onerror=alert(1)> safe resolver failure');
    expect(dialog?.querySelector('img')).toBeNull();

    const dialogButtons = dialog?.querySelectorAll<HTMLButtonElement>('button') ?? [];
    dialogButtons[0]?.focus();
    fixture.componentInstance.trapDialogFocus(
      new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, cancelable: true }),
    );
    expect(document.activeElement).toBe(dialogButtons[dialogButtons.length - 1]);
    fixture.componentInstance.trapDialogFocus(
      new KeyboardEvent('keydown', { key: 'Tab', cancelable: true }),
    );
    expect(document.activeElement).toBe(dialogButtons[0]);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="process-error-details"]')).toBeNull();
  });

  it('keeps file DOM at 20 while paging, clamps same-run snapshots and resets for a new run', () => {
    const fileStatuses = Object.fromEntries(
      Array.from({ length: 100 }, (_, index) => {
        const failed = index === 99;
        const id = `file-${index + 1}`;
        return [
          id,
          {
            id,
            parentScriptId: 'script-1',
            memberName: 'Bank with many files',
            scriptCode: 'SCRIPT_1',
            chunkNumber: index + 1,
            stage: failed ? FileRunStage.Failed : FileRunStage.Written,
            createdAt: '2026-09-21T10:00:00Z',
            queuedAt: '2026-09-21T10:00:00Z',
            stageEnteredAt: '2026-09-21T10:00:00Z',
            updatedAt: '2026-09-21T10:01:00Z',
            completedAt: '2026-09-21T10:01:00Z',
            fileName: `${id}.txt`,
            failure: failed
              ? failure('file', 'writer', 'disk-full', 'safe hidden file failure')
              : null,
          },
        ];
      }),
    );
    activeRun.set({
      correlationId: 'run-file-failure',
      taskCode: 'run',
      status: RunLifecycleStatus.Failed,
      targetCodes: [],
      createdAt: '2026-09-21T10:00:00Z',
      updatedAt: '2026-09-21T10:01:00Z',
      fileStatuses,
    });
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const group = host.querySelector<HTMLElement>('[data-testid^="process-file-group-"]');
    expect(group).not.toBeNull();
    expect(group?.querySelectorAll('.process-file-card')).toHaveLength(0);
    expect(group?.textContent).toContain('Файлов: 100');
    const expand = Array.from(group?.querySelectorAll('button') ?? []).find((button) =>
      button.textContent?.includes('Показать файлы'),
    );
    expand?.click();
    fixture.detectChanges();
    expect(group?.querySelectorAll('.process-file-card')).toHaveLength(20);

    const next = Array.from(group?.querySelectorAll('button') ?? []).find((button) =>
      button.textContent?.includes('Следующие 20'),
    );
    next?.click();
    fixture.detectChanges();
    expect(group?.querySelectorAll('.process-file-card')).toHaveLength(20);
    expect(group?.textContent).toContain('21–40 из 100');
    expect(group?.textContent).toContain('file-21.txt');

    activeRun.update((run) => (run ? { ...run, updatedAt: '2026-09-21T10:02:00Z' } : run));
    fixture.detectChanges();
    expect(host.querySelectorAll('.process-file-card')).toHaveLength(20);
    expect(group?.textContent).toContain('21–40 из 100');

    activeRun.update((run) =>
      run
        ? {
            ...run,
            updatedAt: '2026-09-21T10:03:00Z',
            fileStatuses: Object.fromEntries(Object.entries(fileStatuses).slice(0, 5)),
          }
        : run,
    );
    fixture.detectChanges();
    expect(host.querySelectorAll('.process-file-card')).toHaveLength(5);
    expect(host.textContent).toContain('1–5 из 5');
    expect(fixture.componentInstance.filePageState().pages).toEqual({
      'bank with many files\u0000script-1': 0,
    });

    activeRun.update((run) => (run ? { ...run, correlationId: 'different-run' } : run));
    fixture.detectChanges();
    expect(host.querySelectorAll('.process-file-card')).toHaveLength(0);
  });

  it('shows a run-level failure without entity cards and opens its details', () => {
    activeRun.set({
      correlationId: 'run-level-only',
      taskCode: 'run',
      status: RunLifecycleStatus.Failed,
      targetCodes: [],
      createdAt: '2026-09-21T10:00:00Z',
      updatedAt: '2026-09-21T10:01:00Z',
      failure: failure('run', 'preflight', 'run-preflight', 'safe run failure'),
      memberStatuses: {},
      scriptStatuses: {},
      fileStatuses: {},
      senderBatches: {},
    });
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    const runFailure = host.querySelector<HTMLButtonElement>('[data-testid="process-run-failure"]');
    expect(runFailure).not.toBeNull();
    expect(host.textContent).not.toContain(RU['process.emptyNoCards']);
    runFailure?.click();
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="process-error-details"]')?.textContent).toContain(
      'run-preflight',
    );
  });

  it('counts only active enum states and uses neutral tones for cancelled or skipped cards', () => {
    activeRun.set({
      correlationId: 'terminal-zones',
      taskCode: 'run',
      status: RunLifecycleStatus.Failed,
      targetCodes: [],
      createdAt: '2026-09-21T10:00:00Z',
      updatedAt: '2026-09-21T10:01:00Z',
      memberStatuses: {
        cancelled: {
          memberName: 'Cancelled member',
          status: MemberRunLifecycleStatus.Cancelled,
          lastStep: null,
          message: null,
          updatedAt: '2026-09-21T10:01:00Z',
        },
      },
      scriptStatuses: {
        failed: {
          id: 'failed-script',
          memberName: 'Failed member',
          scriptCode: 'FAILED_SCRIPT',
          stage: ScriptRunStage.Failed,
          discoveredAt: '2026-09-21T10:00:00Z',
          stageEnteredAt: '2026-09-21T10:01:00Z',
          updatedAt: '2026-09-21T10:01:00Z',
        },
        cancelled: {
          id: 'cancelled-script',
          memberName: 'Cancelled member',
          scriptCode: 'CANCELLED_SCRIPT',
          stage: ScriptRunStage.Cancelled,
          discoveredAt: '2026-09-21T10:00:00Z',
          stageEnteredAt: '2026-09-21T10:01:00Z',
          updatedAt: '2026-09-21T10:01:00Z',
        },
      },
      senderBatches: {
        failed: {
          batchId: 'failed-batch',
          memberName: 'Failed member',
          status: SenderBatchStatus.Failed,
          updatedAt: '2026-09-21T10:01:00Z',
          sentFiles: [],
        },
        skipped: {
          batchId: 'skipped-batch',
          memberName: 'Skipped member',
          status: SenderBatchStatus.SkippedByRequest,
          updatedAt: '2026-09-21T10:01:00Z',
          sentFiles: [],
        },
      },
    });
    const fixture = TestBed.createComponent(ProcessRunViewComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.activeCardCount()).toBe(0);
    expect(
      (fixture.componentInstance as unknown as { intervalId: ReturnType<typeof setInterval> | null })
        .intervalId,
    ).toBeNull();
    const cardFor = (text: string) =>
      Array.from(host.querySelectorAll<HTMLElement>('.process-card')).find((card) =>
        card.textContent?.includes(text),
      );
    expect(cardFor('Cancelled member')?.classList.contains('process-tone--neutral')).toBe(true);
    expect(cardFor('CANCELLED_SCRIPT')?.classList.contains('process-tone--neutral')).toBe(true);
    expect(cardFor('Skipped member')?.classList.contains('process-tone--neutral')).toBe(true);
  });
});

function failure(entityType: string, stage: string, code: string, message: string) {
  return {
    entityType,
    entityId: `${entityType}-id`,
    stage,
    code,
    message,
    occurredAt: '2026-09-21T10:01:00Z',
    memberName: null,
    scriptCode: null,
    workerId: null,
    filePath: null,
    chunkNumber: null,
    batchId: null,
  };
}
