import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { RunLifecycleStatus, RunStatusInfo } from '../../app.models';
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
});
