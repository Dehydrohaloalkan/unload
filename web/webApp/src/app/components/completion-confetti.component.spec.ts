import { TestBed } from '@angular/core/testing';
import { RunLifecycleStatus, RunStatusInfo } from '../app.models';
import { CompletionConfettiComponent } from './completion-confetti.component';

describe('CompletionConfettiComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CompletionConfettiComponent],
    }).compileComponents();
  });

  it('celebrates when a running unload completes', async () => {
    const fixture = TestBed.createComponent(CompletionConfettiComponent);
    fixture.componentRef.setInput('run', createRun(RunLifecycleStatus.Running));
    fixture.detectChanges();

    fixture.componentRef.setInput('run', createRun(RunLifecycleStatus.Completed));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.componentInstance.celebrating()).toBe(true);
    expect(fixture.nativeElement.querySelectorAll('.confetti__particle').length).toBe(30);
    fixture.destroy();
  });

  it('does not celebrate a completed unload restored on page load', async () => {
    const fixture = TestBed.createComponent(CompletionConfettiComponent);
    fixture.componentRef.setInput('run', createRun(RunLifecycleStatus.Completed));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(fixture.componentInstance.celebrating()).toBe(false);
  });

  it.each([RunLifecycleStatus.Failed, RunLifecycleStatus.Cancelled])(
    'does not celebrate an unsuccessful unload with status %s',
    async (status) => {
      const fixture = TestBed.createComponent(CompletionConfettiComponent);
      fixture.componentRef.setInput('run', createRun(RunLifecycleStatus.Running));
      fixture.detectChanges();

      fixture.componentRef.setInput('run', createRun(status));
      fixture.detectChanges();
      await fixture.whenStable();

      expect(fixture.componentInstance.celebrating()).toBe(false);
    },
  );
});

function createRun(status: number): RunStatusInfo {
  return {
    correlationId: 'run-1',
    createdAt: '2026-08-21T08:00:00Z',
    status,
    targetCodes: ['target-1'],
    taskCode: 'run',
    updatedAt: '2026-08-21T08:01:00Z',
  };
}
