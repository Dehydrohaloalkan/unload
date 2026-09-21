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

  it('renders rich lifecycle tones and does not duplicate an attached file card', () => {
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
    const stages = host.querySelectorAll<HTMLElement>('.process-stage');

    expect(
      host.querySelector('.process-member-row')?.classList.contains('process-tone--active'),
    ).toBe(true);
    expect(stages[1]?.classList.contains('process-tone--active')).toBe(true);
    expect(stages[2]?.classList.contains('process-tone--danger')).toBe(true);
    expect(stages[3]?.classList.contains('process-tone--success')).toBe(true);
    expect(
      Array.from(host.querySelectorAll('.process-card__title')).filter(
        (card) => card.textContent?.trim() === 'data-file.txt',
      ),
    ).toHaveLength(1);
  });
});
